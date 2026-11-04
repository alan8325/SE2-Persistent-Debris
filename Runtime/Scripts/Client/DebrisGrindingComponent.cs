#nullable enable
using System;
using System.Collections.Generic;
using Keen.Game2.Client.WorldObjects.Tools.Effects;
using Keen.Game2.Simulation.GameSystems.EntityDetection;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.Core.Game.Definitions;
using Keen.VRage.Core.Game.GameSystems.GameTimes;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Accessors;
using Keen.VRage.DCS.Annotations;
using Keen.VRage.DCS.Components;
using Keen.VRage.DCS.ObjectBuilders;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Reflection.DependencyInjections;
using Keen.VRage.Library.UI.PropertyInspector.Visibility;
using Keen.VRage.Multiplayer.Sessions;

namespace PersistentDebris;

[HideInPreview]
public sealed partial class DebrisGrindingComponent : GameComponent, IInSceneListener
{
    private IntegrityToolEffectsComponent _effects = null!;

    [Component("ExactToolOriginProvider", WeakDependency = true)]
    private IToolOriginProvider? _exactOrigin = null;

    [Component("AreaToolDetector", WeakDependency = true)]
    private SphereShapeEntityDetectorComponent? _areaDetector = null;

    [Component("DebrisRaycastDetector", WeakDependency = true)]
    private RaycastEntityDetectorComponent? _rayDetector = null;

    [Component("IInteractedEntityProviderAdapter", WeakDependency = true)]
    private IInteractedEntityProviderAdapter? _interactionProviderAdapter = null;

    [Service]
    private IEntitySpawner _entitySpawner = null!;

    [Service]
    private IGameTime _gameTime = null!;

    [Service]
    private IReplicationMapping _replication = null!;

    private readonly DebrisGrindingProgress _progress = new();
    private readonly HashSet<int> _activeAreaDebrisIds = new();
    private readonly List<DebrisTarget> _areaTargets = new();
    private IntegrityToolDefinition _integrityTool = null!;
    private IDisposable? _handDetectionSubscription;
    private IDisposable? _areaDetectionSubscription;
    private DateTime? _lastUpdate;
    private int? _currentDebrisId;
    private bool _isActive;
    private bool _isVanillaWorking;
    private bool _isGrindingDebris;

    private const string DebrisComponentType = "Keen.VRage.Game.Components.Fractures.DebrisComponent";
    private static readonly Guid FracturedDebrisComposite = new("86dc919d-11c4-405b-8007-a2b4656da355");
    private static readonly Guid DamagedBlockDebrisComposite = new("4162989f-04c1-4e04-953d-febd1077557f");

    private readonly record struct DebrisTarget(Entity Entity, int Id, Vector3D HitPoint, Vector3 HitNormal);

    [Init]
    private void Init()
    {
        Entity.ForEach<IntegrityToolEffectsComponent>(effects => _effects = effects);
        if (_effects == null)
        {
            throw new InvalidOperationException("Grinder has no IntegrityToolEffectsComponent");
        }
        _integrityTool = _replication.GetServerComposite(Entity.GetComposition()).Definition<IntegrityToolComponent, IntegrityToolDefinition>();
    }

    void IInSceneListener.OnAddedToScene()
    {
        IntegrityToolComponent.IsActiveProperty.Subscribe(Entity, OnToolActiveChanged);
        IntegrityToolComponent.IsWorkingProperty.Subscribe(Entity, OnVanillaWorkingChanged);
    }

    void IInSceneListener.OnBeforeRemovedFromScene()
    {
        IntegrityToolComponent.IsActiveProperty.Unsubscribe(Entity, OnToolActiveChanged);
        IntegrityToolComponent.IsWorkingProperty.Unsubscribe(Entity, OnVanillaWorkingChanged);
        StopDetection();
        ResetGrinding();
    }

    private void OnToolActiveChanged(Entity entity, in bool value)
    {
        _isActive = value;
        _lastUpdate = null;
        if (value)
        {
            StartDetection();
        }
        else
        {
            StopDetection();
            ResetGrinding();
        }
    }

    private void OnVanillaWorkingChanged(Entity entity, in bool value)
    {
        _isVanillaWorking = value;
    }

    private void StartDetection()
    {
        if (_areaDetector != null)
        {
            StartAreaDetection();
        }
        else if (_rayDetector != null)
        {
            StartHandDetection();
        }
    }

    private async void StartHandDetection()
    {
        if (_handDetectionSubscription != null)
        {
            return;
        }

        try
        {
            DetectionArgs args = new()
            {
                DetectTopMostEntities = true,
                DetectChildEntities = false,
                DetectProjections = false,
                DetectDummies = false,
                Occlusion = true,
                UsePreciseColliderForBlocks = false,
                FirstHit = true,
                AllowDuplication = false,
                Frequency = 1,
                IgnoredEntity = Entity.GetRoot().DEntity,
            };
            await foreach (DetectionBatch detection in _rayDetector!.StartDetection(args, out _handDetectionSubscription))
            {
                if (!_isActive)
                {
                    continue;
                }

                UpdateHandGrinding(detection, GetElapsed());
            }
        }
        catch (Exception exception)
        {
            Log.Default.WriteLine($"[Persistent Debris] Hand detection failed: {exception}");
            StopDetection();
            ResetGrinding();
        }
    }

    private async void StartAreaDetection()
    {
        if (_areaDetectionSubscription != null)
        {
            return;
        }

        try
        {
            DetectionArgs args = new()
            {
                DetectTopMostEntities = true,
                DetectChildEntities = false,
                DetectProjections = false,
                DetectDummies = false,
                Occlusion = false,
                UsePreciseColliderForBlocks = false,
                FirstHit = false,
                AllowDuplication = false,
                Frequency = 1,
            };
            await foreach (DetectionBatch detection in _areaDetector!.StartDetection(args, out _areaDetectionSubscription))
            {
                if (!_isActive)
                {
                    continue;
                }

                UpdateAreaGrinding(detection, GetElapsed());
            }
        }
        catch (Exception exception)
        {
            Log.Default.WriteLine($"[Persistent Debris] Area detection failed: {exception}");
            StopDetection();
            ResetGrinding();
        }
    }

    private TimeSpan GetElapsed()
    {
        DateTime now = _gameTime.CurrentGameTime;
        TimeSpan elapsed = _lastUpdate is DateTime previous ? now - previous : TimeSpan.Zero;
        _lastUpdate = now;
        return elapsed;
    }

    private void UpdateHandGrinding(DetectionBatch detection, TimeSpan elapsed)
    {
        Entity? debris = null;
        for (int i = 0; i < detection.Entities.Length; i++)
        {
            if (IsDebris(detection.Entities[i]))
            {
                debris = detection.Entities[i];
                break;
            }
        }

        if (debris == null)
        {
            ResetGrinding();
            return;
        }

        int debrisId = debris.DEntity.GetHashCode();
        if (_currentDebrisId != debrisId)
        {
            _progress.Clear();
            _currentDebrisId = debrisId;
        }

        _isGrindingDebris = true;
        _effects.IntegrityToolIsChangingBlock(false, detection.Positions[0], detection.Normals[0], -1f);
        if (_progress.Advance(debrisId, elapsed))
        {
            _entitySpawner.DeleteEntity(debris);
            ResetGrinding();
        }
    }

    private void UpdateAreaGrinding(DetectionBatch detection, TimeSpan elapsed)
    {
        if (!TryGetAreaInteractionPoint(out _, out _))
        {
            ResetGrinding();
            return;
        }

        _activeAreaDebrisIds.Clear();
        _areaTargets.Clear();
        for (int i = 0; i < detection.Entities.Length; i++)
        {
            Entity debris = detection.Entities[i];
            if (IsDebris(debris))
            {
                int debrisId = debris.DEntity.GetHashCode();
                _activeAreaDebrisIds.Add(debrisId);
                _areaTargets.Add(new DebrisTarget(debris, debrisId, detection.Positions[i], detection.Normals[i]));
            }
        }

        _progress.Retain(_activeAreaDebrisIds);
        if (_areaTargets.Count == 0)
        {
            ResetGrinding();
            return;
        }

        _isGrindingDebris = true;
        bool effectShown = false;
        int remainingTargets = 0;
        foreach (DebrisTarget target in _areaTargets)
        {
            if (!effectShown)
            {
                _effects.IntegrityToolIsChangingBlock(false, target.HitPoint, target.HitNormal, -1f);
                effectShown = true;
            }

            if (_progress.Advance(target.Id, elapsed))
            {
                _entitySpawner.DeleteEntity(target.Entity);
            }
            else
            {
                remainingTargets++;
            }
        }

        if (remainingTargets == 0)
        {
            ResetGrinding();
        }
    }

    private static bool IsDebris(Entity entity)
    {
        Guid compositionId = entity.GetComposition().Guid;
        if (compositionId != FracturedDebrisComposite && compositionId != DamagedBlockDebrisComposite)
        {
            return false;
        }

        bool found = false;
        entity.ForEach<Component>(component =>
        {
            if (component.GetType().FullName == DebrisComponentType)
            {
                found = true;
            }
        });
        return found;
    }

    private bool TryGetAreaInteractionPoint(out Vector3D interactionPoint, out Vector3 interactionNormal)
    {
        IInteractedEntityProvider? provider = _interactionProviderAdapter?.InteractedEntityProvider;
        Vector3D? point = provider?.InteractionPoint;
        Vector3? normal = provider?.InteractionNormal;
        if (point is Vector3D validPoint && normal is Vector3 validNormal)
        {
            WorldTransform toolTransform = _exactOrigin?.WorldTransform ?? Entity.Data.GetWorldTransform();
            if (Vector3D.Distance(validPoint, toolTransform.Position) <= _integrityTool.MaxInteractionDistance)
            {
                interactionPoint = validPoint;
                interactionNormal = validNormal;
                return true;
            }
        }

        interactionPoint = default;
        interactionNormal = default;
        return false;
    }

    private void StopDetection()
    {
        _handDetectionSubscription?.Dispose();
        _handDetectionSubscription = null;
        _areaDetectionSubscription?.Dispose();
        _areaDetectionSubscription = null;
    }

    private void ResetGrinding()
    {
        _progress.Clear();
        _activeAreaDebrisIds.Clear();
        _areaTargets.Clear();
        _currentDebrisId = null;
        if (_isGrindingDebris && !_isVanillaWorking)
        {
            _effects.OnToolWorking(false);
        }
        _isGrindingDebris = false;
    }
}
