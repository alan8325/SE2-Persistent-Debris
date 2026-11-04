#nullable enable
using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.GameSystems.EntityDetection;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Definitions;
using Keen.VRage.DCS.Definitions;
using Keen.VRage.DCS.ObjectBuilders;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Serialization.ObjectBuilders;

namespace PersistentDebris;

[DefinitionPostProcessor]
public sealed class DebrisGrindingInjector : SimpleDefinitionPostProcessor<EntityCompositeDefinition>
{
    private static readonly Guid HandComponent = new("aa100000-0000-4000-8000-000000000101");
    private static readonly Guid HandRayComponent = new("aa100000-0000-4000-8000-000000000300");
    private static readonly Guid Ship350Detector = new("aa100000-0000-4000-8000-000000000201");
    private static readonly Guid Ship350Component = new("aa100000-0000-4000-8000-000000000202");
    private static readonly Guid Ship400Detector = new("aa100000-0000-4000-8000-000000000203");
    private static readonly Guid Ship400Component = new("aa100000-0000-4000-8000-000000000204");
    private static readonly Guid Ship350DetectorDefinition = new("33f8a7b6-4e53-4aff-93bf-0a44b48497b1");
    private static readonly Guid Ship400DetectorDefinition = new("a654a626-ff0f-41cb-afd5-cae32a1c3a16");

    private static readonly Dictionary<Guid, Guid[]> GrinderComponents = new()
    {
        [new Guid("a11c2ed0-8431-4150-b6f3-ecb0938c9949")] = new[] { HandRayComponent, HandComponent },
        [new Guid("ad8a7eb9-6de9-448a-af22-4108a996baf6")] = new[] { HandRayComponent, HandComponent },
        [new Guid("f373d9d4-9973-4d37-80ee-2c9503492d0c")] = new[] { HandRayComponent, HandComponent },
        [new Guid("15348d51-07f8-444d-b945-c981a1bbd109")] = new[] { HandRayComponent, HandComponent },
        [new Guid("c5edc178-3381-48ec-a4e8-fb5c16fc0c83")] = new[] { Ship350Detector, Ship350Component },
        [new Guid("c21ced4f-d2ca-4022-8c4d-afdf8adab50d")] = new[] { Ship400Detector, Ship400Component },
    };

    private static readonly Dictionary<Guid, Guid> DetectorDefinitions = new()
    {
        [Ship350Detector] = Ship350DetectorDefinition,
        [Ship400Detector] = Ship400DetectorDefinition,
    };

    private static readonly Dictionary<Guid, Guid> HandRayDefinitions = new()
    {
        [new Guid("a11c2ed0-8431-4150-b6f3-ecb0938c9949")] = new Guid("aa100000-0000-4000-8000-000000000311"),
        [new Guid("ad8a7eb9-6de9-448a-af22-4108a996baf6")] = new Guid("aa100000-0000-4000-8000-000000000312"),
        [new Guid("f373d9d4-9973-4d37-80ee-2c9503492d0c")] = new Guid("aa100000-0000-4000-8000-000000000313"),
        [new Guid("15348d51-07f8-444d-b945-c981a1bbd109")] = new Guid("aa100000-0000-4000-8000-000000000314"),
    };

    private static readonly Dictionary<Guid, Guid> HandServerComposites = new()
    {
        [new Guid("a11c2ed0-8431-4150-b6f3-ecb0938c9949")] = new Guid("ff1b4a1b-694f-4b2c-8f2c-64a7b474dd63"),
        [new Guid("ad8a7eb9-6de9-448a-af22-4108a996baf6")] = new Guid("905e083f-fa8d-40f3-b507-fc9a49e0e472"),
        [new Guid("f373d9d4-9973-4d37-80ee-2c9503492d0c")] = new Guid("8e379e71-88ce-47fb-977e-bc64134efbe1"),
        [new Guid("15348d51-07f8-444d-b945-c981a1bbd109")] = new Guid("b8a27865-1a23-4f7b-8739-ee94c8ff3c65"),
    };

    public override void PostProcess(EntityCompositeDefinition definition)
    {
        if (!GrinderComponents.TryGetValue(definition.Guid, out Guid[]? componentIds))
        {
            return;
        }

        try
        {
            bool isShipGrinder = definition.Guid == new Guid("c5edc178-3381-48ec-a4e8-fb5c16fc0c83")
                || definition.Guid == new Guid("c21ced4f-d2ca-4022-8c4d-afdf8adab50d");
            Definition detectorDefinition;
            Type detectorType;

            if (isShipGrinder)
            {
                Guid detectorDefinitionId = DetectorDefinitions[componentIds[0]];
                if (!Manager.TryGetDefinition<Definition>(detectorDefinitionId, out Definition? sphereDefinition))
                {
                    throw new InvalidOperationException($"Missing detector definition {detectorDefinitionId}");
                }
                detectorDefinition = sphereDefinition;
                detectorType = typeof(SphereShapeEntityDetectorComponent);
            }
            else
            {
                Guid serverCompositeId = HandServerComposites[definition.Guid];
                if (!Manager.TryGetDefinition<EntityCompositeDefinition>(serverCompositeId, out EntityCompositeDefinition? serverComposite))
                {
                    throw new InvalidOperationException($"Missing server composite {serverCompositeId}");
                }
                IntegrityToolDefinition integrityTool = serverComposite.Definition<IntegrityToolComponent, IntegrityToolDefinition>();
                var rayBuilder = new RaycastEntityDetectorDefinitionObjectBuilder
                {
                    Guid = HandRayDefinitions[definition.Guid],
                    MaxLength = integrityTool.MaxInteractionDistance,
                };
                var rayDefinition = new RaycastEntityDetectorDefinition();
                ObjectBuilder.InvokeInit(rayDefinition, rayBuilder);
                detectorDefinition = rayDefinition;
                detectorType = typeof(RaycastEntityDetectorComponent);
            }

            DebrisGrindingInjection.Apply(
                definition,
                componentIds,
                detectorType,
                detectorDefinition,
                isShipGrinder,
                typeof(DebrisGrindingComponent),
                Manager.GetDefinitionsOfTypeAndDescendants<PrefabDefinition>());
        }
        catch (Exception exception)
        {
            Log.Default.WriteLine($"[Persistent Debris] Grinder injection failed for {definition.Guid}: {exception}");
        }
    }
}
