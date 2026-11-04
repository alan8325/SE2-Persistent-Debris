#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Keen.VRage.Core.Game.Definitions;
using Keen.VRage.DCS.Definitions;
using Keen.VRage.DCS.ObjectBuilders;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Utils;

namespace PersistentDebris;

public static class DebrisGrindingInjection
{
    public static void Apply(
        EntityCompositeDefinition composition,
        Guid[] componentIds,
        Type detectorType,
        Definition detectorDefinition,
        bool isShipGrinder,
        Type grindingComponentType,
        IEnumerable<PrefabDefinition> prefabs)
    {
        int originalCount = composition.ComponentCount;
        EntityCompositeDefinition.Builder builder = Copy(composition, originalCount + componentIds.Length);
        int index = originalCount;
        StringId detectorTag = StringId.Get(isShipGrinder ? "AreaToolDetector" : "DebrisRaycastDetector");
        AddIfMissing(builder, ref index, componentIds[0], detectorType, detectorDefinition, detectorTag);
        AddIfMissing(builder, ref index, componentIds[^1], grindingComponentType, null, null);

        if (!isShipGrinder)
        {
            AddOriginTag(builder);
        }

        builder.InvalidateTagIndex();
        _ = builder.TagIndex;

        CompositionReflection.Instance.Assign(composition, builder);
        AddObjectBuilderSlots(composition.Guid, componentIds, prefabs);
    }

    private static void AddOriginTag(EntityCompositeDefinition.Builder builder)
    {
        StringId exactOrigin = StringId.Get("ExactToolOriginProvider");
        StringId raycastOrigin = StringId.Get("RaycastOriginProvider");
        if (!builder.TagIndex.TryGetValue(exactOrigin, out int index)
            || builder.TagIndex.ContainsKey(raycastOrigin))
        {
            return;
        }

        CompositionReflection reflection = CompositionReflection.Instance;
        Array tags = reflection.TagsOf(builder);
        object tagSet = tags.GetValue(index)!;
        tags.SetValue(reflection.AddTag(tagSet, raycastOrigin), index);
    }

    private static void AddIfMissing(
        EntityCompositeDefinition.Builder builder,
        ref int index,
        Guid id,
        Type type,
        Definition? definition,
        StringId? tag)
    {
        for (int i = 0; i < builder.ComponentCount; i++)
        {
            if (builder.Types[i] == type)
            {
                return;
            }
        }

        CompositionReflection reflection = CompositionReflection.Instance;
        Array tags = reflection.TagsOf(builder);
        object emptyTagSet = reflection.EmptyTagSet(tags.GetValue(index)!);
        object tagSet = tag is StringId value ? reflection.AddTag(emptyTagSet, value) : emptyTagSet;

        builder.Ids[index] = id;
        builder.Types[index] = type;
        builder.Definitions[index] = definition;
        tags.SetValue(tagSet, index);
        index++;
    }

    private static void AddObjectBuilderSlots(
        Guid compositeId,
        Guid[] componentIds,
        IEnumerable<PrefabDefinition> prefabs)
    {
        foreach (PrefabDefinition prefab in prefabs)
        {
            EntityObjectBuilder? entity = CompositionReflection.Instance.EntityOf(prefab);
            if (entity?.Definition?.Guid != compositeId)
            {
                continue;
            }

            foreach (Guid componentId in componentIds)
            {
                if (!entity.ObjectBuilders.Keys.Contains(componentId))
                {
                    entity.ObjectBuilders.Add(componentId, null);
                }
            }
        }
    }

    private static EntityCompositeDefinition.Builder Copy(EntityCompositeDefinition composition, int count)
    {
        var builder = new EntityCompositeDefinition.Builder(count);
        Array destinationTags = CompositionReflection.Instance.TagsOf(builder);
        for (int i = 0; i < composition.ComponentCount; i++)
        {
            builder.Ids[i] = composition.Ids[i];
            builder.Types[i] = composition.Types[i];
            builder.Definitions[i] = composition.Definitions[i];
            destinationTags.SetValue(CompositionReflection.Instance.TagSetAt(composition, i), i);
        }
        return builder;
    }
}

internal sealed class CompositionReflection
{
    internal static readonly CompositionReflection Instance = new();

    private readonly MethodInfo? _assign = typeof(EntityCompositeDefinition).GetMethod(
        "Assign",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[] { typeof(EntityCompositeDefinition.Builder) },
        null);
    private readonly FieldInfo? _prefabEntity = typeof(PrefabDefinition).GetField(
        "_entity",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly PropertyInfo? _compositionTags = typeof(EntityCompositeDefinition).GetProperty("Tags");
    private readonly FieldInfo? _builderTags = typeof(EntityCompositeDefinition.Builder).GetField("Tags");

    private CompositionReflection()
    {
    }

    internal EntityObjectBuilder? EntityOf(PrefabDefinition prefab)
    {
        return _prefabEntity?.GetValue(prefab) as EntityObjectBuilder;
    }

    internal object TagSetAt(EntityCompositeDefinition composition, int index)
    {
        object tags = _compositionTags?.GetValue(composition)
            ?? throw new MissingFieldException(nameof(EntityCompositeDefinition), "Tags");
        return tags.GetType().GetMethod("get_Item")?.Invoke(tags, new object[] { index })
            ?? throw new MissingMethodException(tags.GetType().FullName, "get_Item");
    }

    internal Array TagsOf(EntityCompositeDefinition.Builder builder)
    {
        if (_builderTags?.GetValue(builder) is not Array tags)
        {
            throw new MissingFieldException(nameof(EntityCompositeDefinition.Builder), "Tags");
        }
        return tags;
    }

    internal object EmptyTagSet(object tagSet)
    {
        return tagSet.GetType().GetField("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new MissingMemberException(tagSet.GetType().FullName, "Empty");
    }

    internal object AddTag(object tagSet, StringId tag)
    {
        return tagSet.GetType().GetMethod("Add")?.Invoke(tagSet, new object[] { tag })
            ?? throw new MissingMethodException(tagSet.GetType().FullName, "Add");
    }

    internal void Assign(EntityCompositeDefinition composition, EntityCompositeDefinition.Builder builder)
    {
        if (_assign == null)
        {
            throw new MissingMethodException(nameof(EntityCompositeDefinition), "Assign");
        }
        _assign.Invoke(composition, new object[] { builder });
    }
}
