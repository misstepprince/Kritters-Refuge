using System.Numerics;
using Content.Server._DV.Mail.Components;
using Content.Server._Kritters.SpaceCleanup.Components;
using Content.Server._NF.GC.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Construction.Components;
using Content.Shared._Kritters.CCVar;
using Content.Shared.Construction.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Server.Player;

namespace Content.Server._Kritters.SpaceCleanup;

/// <summary>
/// Rate-limited cleanup for abandoned, loose entities in open space.
/// </summary>
public sealed partial class AggressiveSpaceJanitorSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PricingSystem _pricing = default!;

    private bool _enabled = true;
    private TimeSpan _scanInterval = TimeSpan.FromMinutes(1);
    private TimeSpan _lowValueLifetime = TimeSpan.FromMinutes(30);
    private TimeSpan _highValueLifetime = TimeSpan.FromMinutes(60);
    private TimeSpan _gridLowValueLifetime = TimeSpan.FromMinutes(5);
    private TimeSpan _gridHighValueLifetime = TimeSpan.FromMinutes(15);
    private TimeSpan _mailLifetime = TimeSpan.FromHours(5);
    private TimeSpan _nextScan;
    private double _highValueThreshold = 500;
    private float _playerRadius = 10f;
    private int _deletionLimit = 64;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorEnabled, SetEnabled, true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorScanIntervalSeconds, value => _scanInterval = TimeSpan.FromSeconds(Math.Max(1, value)), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorLowValueLifetimeSeconds, value => _lowValueLifetime = TimeSpan.FromSeconds(Math.Max(1, value)), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorHighValueLifetimeSeconds, value => _highValueLifetime = TimeSpan.FromSeconds(Math.Max(1, value)), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorGridLowValueLifetimeSeconds, value => _gridLowValueLifetime = TimeSpan.FromSeconds(Math.Max(1, value)), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorGridHighValueLifetimeSeconds, value => _gridHighValueLifetime = TimeSpan.FromSeconds(Math.Max(1, value)), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorMailLifetimeSeconds, value => _mailLifetime = TimeSpan.FromSeconds(Math.Max(1, value)), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorHighValueThreshold, value => _highValueThreshold = Math.Max(0, value), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorPlayerRadius, value => _playerRadius = Math.Max(0, value), true);
        Subs.CVar(_cfg, KrittersCCVars.AggressiveSpaceJanitorDeletionLimit, value => _deletionLimit = Math.Max(1, value), true);
    }

    public override void Update(float frameTime)
    {
        if (!_enabled || _timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + _scanInterval;
        RunCleanup();
    }

    private void SetEnabled(bool value)
    {
        _enabled = value;
        if (value)
            return;

        var query = EntityQueryEnumerator<AggressiveSpaceJanitorTrackedComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!Deleted(uid) && !Terminating(uid))
                RemCompDeferred<AggressiveSpaceJanitorTrackedComponent>(uid);
        }
    }

    /// <summary>
    /// Performs one normal, safeguards-preserving cleanup evaluation.
    /// </summary>
    public int RunCleanup()
    {
        return RunCleanup(null, expedited: false);
    }

    /// <summary>
    /// Starts or advances expedited cleanup timers for loose entities in open space.
    /// </summary>
    public int RunSpaceCleanup()
    {
        return RunCleanup(null, expedited: true);
    }

    /// <summary>
    /// Stops all pending expedited cleanup timers for loose entities in open space.
    /// </summary>
    public int CancelSpaceCleanup()
    {
        var cancelled = 0;
        var query = EntityQueryEnumerator<AggressiveSpaceJanitorTrackedComponent>();
        while (query.MoveNext(out var uid, out var tracker))
        {
            if (tracker.TargetGrid != null || !tracker.Expedited || Deleted(uid) || Terminating(uid))
                continue;

            RemComp<AggressiveSpaceJanitorTrackedComponent>(uid);
            cancelled++;
        }

        return cancelled;
    }

    /// <summary>
    /// Starts or advances expedited cleanup timers for loose entities on a single grid.
    /// </summary>
    public int RunGridCleanup(EntityUid grid)
    {
        return RunCleanup(grid, expedited: true);
    }

    /// <summary>
    /// Stops all pending expedited cleanup timers for a grid.
    /// </summary>
    public int CancelGridCleanup(EntityUid grid)
    {
        var cancelled = 0;
        var query = EntityQueryEnumerator<AggressiveSpaceJanitorTrackedComponent>();
        while (query.MoveNext(out var uid, out var tracker))
        {
            if (tracker.TargetGrid != grid || Deleted(uid) || Terminating(uid))
                continue;

            RemComp<AggressiveSpaceJanitorTrackedComponent>(uid);
            cancelled++;
        }

        return cancelled;
    }

    /// <summary>
    /// Immediately queues every loose non-mob entity on a grid. This intentionally bypasses all item safeguards.
    /// </summary>
    public int ForceGridCleanup(EntityUid grid)
    {
        return ForceCleanup(grid);
    }

    /// <summary>
    /// Immediately queues every loose non-mob entity drifting in open space. This intentionally bypasses all item safeguards.
    /// </summary>
    public int ForceSpaceCleanup()
    {
        return ForceCleanup(null);
    }

    /// <summary>
    /// Returns the number of entities eligible for an immediate cleanup in the requested scope.
    /// </summary>
    public int GetForceEligibleCount(EntityUid? grid = null)
    {
        var count = 0;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (!Deleted(uid) && !Terminating(uid) && IsForceEligible(uid, xform, grid))
                count++;
        }

        return count;
    }

    private int ForceCleanup(EntityUid? grid)
    {
        var deleted = 0;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid) || Terminating(uid) || !IsForceEligible(uid, xform, grid))
                continue;

            QueueDel(uid);
            deleted++;
        }

        return deleted;
    }

    private int RunCleanup(EntityUid? grid, bool expedited)
    {
        if (!_enabled)
            return 0;

        var now = _timing.CurTime;
        var deleted = 0;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid) || Terminating(uid))
                continue;

            TryComp<AggressiveSpaceJanitorTrackedComponent>(uid, out var tracker);
            var targetGrid = grid ?? tracker?.TargetGrid;
            var targetExpedited = grid != null ? expedited : tracker?.Expedited ?? expedited;

            if (!IsEligible(uid, xform, now, targetGrid, targetExpedited, out var lifetime))
            {
                RemCompDeferred<AggressiveSpaceJanitorTrackedComponent>(uid);
                continue;
            }

            tracker ??= EnsureComp<AggressiveSpaceJanitorTrackedComponent>(uid);
            if (!tracker.Started)
            {
                tracker.Started = true;
                tracker.LastAccountedAt = now;
                tracker.Remaining = lifetime;
                tracker.TargetGrid = targetGrid;
                tracker.Expedited = targetExpedited;
                continue;
            }

            if (targetExpedited)
                tracker.Expedited = true;

            if (targetGrid != null)
                tracker.TargetGrid = targetGrid;

            if (targetExpedited && tracker.Remaining > lifetime)
                tracker.Remaining = lifetime;

            var elapsed = now - tracker.LastAccountedAt;
            tracker.LastAccountedAt = now;
            if (HasNearbyPlayer(uid))
                continue;

            tracker.Remaining -= elapsed;
            if (tracker.Remaining > TimeSpan.Zero || deleted >= _deletionLimit)
                continue;

            QueueDel(uid);
            deleted++;
        }

        return deleted;
    }

    private bool IsEligible(EntityUid uid, TransformComponent xform, TimeSpan now, EntityUid? grid, bool expedited, out TimeSpan lifetime)
    {
        lifetime = TimeSpan.Zero;
        if (xform.MapID == MapId.Nullspace || xform.MapUid == null || xform.Anchored)
            return false;

        if (grid is { } targetGrid && xform.GridUid != targetGrid)
            return false;

        if (grid == null && xform.GridUid != null)
            return false;

        if (_containers.IsEntityOrParentInContainer(uid, xform: xform))
            return false;

        if (HasComp<MapComponent>(uid)
            || HasComp<MapGridComponent>(uid)
            || HasComp<MobStateComponent>(uid)
            || HasComp<ActorComponent>(uid)
            || HasComp<DeletionCensusExemptComponent>(uid)
            || HasComp<AggressiveSpaceJanitorExemptComponent>(uid)
            || HasComp<MachineComponent>(uid)
            || HasComp<ComputerComponent>(uid))
        {
            return false;
        }

        if (TryComp<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
            return false;

        if (TryComp<MailComponent>(uid, out var mail))
        {
            if (!mail.IsEnabled)
            {
                lifetime = expedited ? _gridLowValueLifetime : _lowValueLifetime;
                return true;
            }

            var eligibleAt = mail.IsPriority && mail.PriorityExpiryTime > mail.TrashTime
                ? mail.PriorityExpiryTime
                : mail.TrashTime;
            if (mail.HasBeenPickedUp || now < eligibleAt)
                return false;

            lifetime = _mailLifetime;
            return true;
        }

        if (HasContents(uid))
            return false;

        var highValue = _pricing.GetPrice(uid, includeContents: false) >= _highValueThreshold;
        lifetime = highValue
            ? expedited ? _gridHighValueLifetime : _highValueLifetime
            : expedited ? _gridLowValueLifetime : _lowValueLifetime;
        return true;
    }

    private bool IsForceEligible(EntityUid uid, TransformComponent xform, EntityUid? grid)
    {
        if (xform.Anchored || _containers.IsEntityOrParentInContainer(uid, xform: xform))
            return false;

        if (grid is { } targetGrid)
        {
            if (xform.GridUid != targetGrid)
                return false;
        }
        else if (xform.MapID == MapId.Nullspace || xform.MapUid == null || xform.GridUid != null)
        {
            return false;
        }

        if (HasComp<MapComponent>(uid)
            || HasComp<MapGridComponent>(uid)
            || HasComp<MobStateComponent>(uid)
            || HasComp<ActorComponent>(uid))
        {
            return false;
        }

        return !TryComp<MindContainerComponent>(uid, out var mindContainer) || !mindContainer.HasMind;
    }

    private bool HasContents(EntityUid uid)
    {
        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return false;

        return manager.Containers.Values.Any(container => container.ContainedEntities.Count > 0);
    }

    private bool HasNearbyPlayer(EntityUid uid)
    {
        var itemPosition = _transform.GetMapCoordinates(uid);
        foreach (var session in Filter.GetAllPlayers(_playerManager))
        {
            if (session.AttachedEntity is not { Valid: true } player
                || HasComp<GhostComponent>(player)
                || !HasComp<ActorComponent>(player)
                || !HasComp<MobStateComponent>(player))
            {
                continue;
            }

            var playerPosition = _transform.GetMapCoordinates(player);
            if (playerPosition.MapId != itemPosition.MapId)
                continue;

            if (Vector2.DistanceSquared(itemPosition.Position, playerPosition.Position) <= _playerRadius * _playerRadius)
                return true;
        }

        return false;
    }
}
