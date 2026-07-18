using System.Numerics;
using Content.Server._DV.Mail.Components;
using Content.Server._Kritters.SpaceCleanup.Components;
using Content.Server._NF.GC.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Construction.Components;
using Content.Shared._Kritters.CCVar;
using Content.Shared.Construction.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared._Goobstation.Vehicles;
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
    [Dependency] private IChatManager _chat = default!;

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
        var deleted = RunCleanup();
        if (deleted == 0)
            return;

        var report = $"Bluespace Janitorial Services cleanup sweep queued {deleted} entities for deletion.";
        Log.Info(report);
        _chat.SendAdminAnnouncement(report);
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
    /// Immediately queues loose instances of an exact prototype on a grid, while preserving player and container safety.
    /// </summary>
    public int ForceGridPrototypeCleanup(EntityUid grid, string prototypeId)
    {
        return ForceGridPrototypeCleanup(grid, new HashSet<string>(StringComparer.Ordinal) { prototypeId });
    }

    /// <summary>
    /// Immediately queues loose instances of exact prototypes on a grid, while preserving player and container safety.
    /// </summary>
    public int ForceGridPrototypeCleanup(EntityUid grid, IReadOnlySet<string> prototypeIds)
    {
        return ForcePrototypeCleanup(grid, prototypeIds);
    }

    /// <summary>
    /// Immediately queues loose instances of an exact prototype in open space, while preserving player and container safety.
    /// </summary>
    public int ForceSpacePrototypeCleanup(string prototypeId)
    {
        return ForceSpacePrototypeCleanup(new HashSet<string>(StringComparer.Ordinal) { prototypeId });
    }

    /// <summary>
    /// Immediately queues loose instances of exact prototypes in open space, while preserving player and container safety.
    /// </summary>
    public int ForceSpacePrototypeCleanup(IReadOnlySet<string> prototypeIds)
    {
        return ForcePrototypeCleanup(null, prototypeIds);
    }

    /// <summary>
    /// Adds or removes the janitor exemption from live instances of exact prototypes on a grid.
    /// </summary>
    public int SetGridPrototypeExemption(EntityUid grid, IReadOnlySet<string> prototypeIds, bool exempt)
    {
        var matches = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid)
                || Terminating(uid)
                || xform.GridUid != grid
                || MetaData(uid).EntityPrototype?.ID is not { } prototypeId
                || !prototypeIds.Contains(prototypeId))
            {
                continue;
            }

            matches.Add(uid);
        }

        var affected = 0;
        foreach (var uid in matches)
        {
            if (exempt)
            {
                if (HasComp<AggressiveSpaceJanitorExemptComponent>(uid))
                    continue;

                EnsureComp<AggressiveSpaceJanitorExemptComponent>(uid);
                RemComp<AggressiveSpaceJanitorTrackedComponent>(uid);
            }
            else
            {
                if (!HasComp<AggressiveSpaceJanitorExemptComponent>(uid))
                    continue;

                RemComp<AggressiveSpaceJanitorExemptComponent>(uid);
            }

            affected++;
        }

        return affected;
    }

    /// <summary>
    /// Returns live entities matching all requested prototype and component filters.
    /// </summary>
    public List<SpaceJanitorInspectionEntry> GetInspectionEntries(
        IReadOnlyList<string> prototypeFilters,
        IReadOnlyList<Type> componentFilters)
    {
        var entries = new List<SpaceJanitorInspectionEntry>();
        var counts = new Dictionary<(string Prototype, EntityUid? Grid, MapId Map), int>();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid) || Terminating(uid))
                continue;

            var prototypeId = MetaData(uid).EntityPrototype?.ID ?? "<no prototype>";
            if (!prototypeFilters.All(filter => prototypeId.Contains(filter, StringComparison.OrdinalIgnoreCase))
                || componentFilters.Any(component => !TryComp(uid, component, out _)))
            {
                continue;
            }

            var eligible = IsEligible(uid, xform, now, null, expedited: false, out var lifetime, out var reason);
            TimeSpan? remaining = eligible
                ? TryComp<AggressiveSpaceJanitorTrackedComponent>(uid, out var tracker) && tracker.Started
                    ? tracker.Remaining
                    : lifetime
                : null;
            var grid = xform.GridUid;
            var position = grid is { } gridUid
                ? _transform.ToCoordinates(gridUid, _transform.GetMapCoordinates(uid, xform: xform)).Position
                : _transform.GetWorldPosition(xform);
            var scope = (prototypeId, grid, xform.MapID);
            counts.TryGetValue(scope, out var count);
            counts[scope] = count + 1;
            entries.Add(new SpaceJanitorInspectionEntry(uid, prototypeId, grid, xform.MapID, position, remaining, reason, 0));
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entries[i] = entry with
            {
                ScopePrototypeCount = counts[(entry.PrototypeId, entry.Grid, entry.MapId)],
            };
        }

        return entries;
    }

    private int ForcePrototypeCleanup(EntityUid? grid, IReadOnlySet<string> prototypeIds)
    {
        var deleted = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid)
                || Terminating(uid)
                || !IsInScope(xform, grid)
                || !IsPrototypeCullEligible(uid, xform, prototypeIds))
            {
                continue;
            }

            deleted.Add(uid);
        }

        foreach (var uid in deleted)
        {
            QueueDel(uid);
        }

        return deleted.Count;
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
        var deleted = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid) || Terminating(uid) || !IsForceEligible(uid, xform, grid))
                continue;

            deleted.Add(uid);
        }

        foreach (var uid in deleted)
        {
            QueueDel(uid);
        }

        return deleted.Count;
    }

    private int RunCleanup(EntityUid? grid, bool expedited)
    {
        if (!_enabled)
            return 0;

        var now = _timing.CurTime;
        var deleted = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (Deleted(uid) || Terminating(uid))
                continue;

            TryComp<AggressiveSpaceJanitorTrackedComponent>(uid, out var tracker);
            var targetGrid = grid ?? tracker?.TargetGrid;
            var targetExpedited = grid != null ? expedited : tracker?.Expedited ?? expedited;

            if (!IsEligible(uid, xform, now, targetGrid, targetExpedited, out var lifetime, out _))
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
            if (tracker.Remaining > TimeSpan.Zero || deleted.Count >= _deletionLimit)
                continue;

            deleted.Add(uid);
        }

        foreach (var uid in deleted)
        {
            QueueDel(uid);
        }

        return deleted.Count;
    }

    private bool IsEligible(
        EntityUid uid,
        TransformComponent xform,
        TimeSpan now,
        EntityUid? grid,
        bool expedited,
        out TimeSpan lifetime,
        out string? ineligibilityReason)
    {
        lifetime = TimeSpan.Zero;
        ineligibilityReason = null;
        if (xform.MapID == MapId.Nullspace || xform.MapUid == null || xform.Anchored)
        {
            ineligibilityReason = xform.Anchored ? "anchored" : "not on a map";
            return false;
        }

        if (HasComp<MapComponent>(uid)
            || HasComp<MapGridComponent>(uid)
            || HasComp<MobStateComponent>(uid)
            || HasComp<ActorComponent>(uid)
            || HasComp<DeletionCensusExemptComponent>(uid)
            || HasComp<AggressiveSpaceJanitorExemptComponent>(uid)
            || HasComp<MachineComponent>(uid)
            || HasComp<ComputerComponent>(uid))
        {
            ineligibilityReason = GetProtectedEntityReason(uid);
            return false;
        }

        if (TryComp<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
        {
            ineligibilityReason = "has a mind";
            return false;
        }

        if (grid is { } targetGrid && xform.GridUid != targetGrid)
        {
            ineligibilityReason = "outside selected grid";
            return false;
        }

        if (grid == null && xform.GridUid != null)
        {
            ineligibilityReason = "on a grid";
            return false;
        }

        if (_containers.IsEntityOrParentInContainer(uid, xform: xform))
        {
            ineligibilityReason = "contained";
            return false;
        }

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
            {
                ineligibilityReason = "mail is not ready for cleanup";
                return false;
            }

            lifetime = _mailLifetime;
            return true;
        }

        if (HasContents(uid))
        {
            ineligibilityReason = "contains entities";
            return false;
        }

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

    private static bool IsInScope(TransformComponent xform, EntityUid? grid)
    {
        if (grid is { } targetGrid)
            return xform.GridUid == targetGrid;

        return xform.MapID != MapId.Nullspace && xform.MapUid != null && xform.GridUid == null;
    }

    private bool IsPrototypeCullEligible(EntityUid uid, TransformComponent xform, IReadOnlySet<string> prototypeIds)
    {
        if (xform.Anchored
            || _containers.IsEntityOrParentInContainer(uid, xform: xform)
            || HasComp<MapComponent>(uid)
            || HasComp<MapGridComponent>(uid)
            || HasComp<ActorComponent>(uid)
            || HasComp<AggressiveSpaceJanitorExemptComponent>(uid))
        {
            return false;
        }

        if (TryComp<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
            return false;

        // NPC bodies contain organs, so only protect contained entities on non-mobs.
        if (!HasComp<MobStateComponent>(uid) && HasContents(uid))
            return false;

        if (TryComp<VehicleComponent>(uid, out var vehicle)
            && (vehicle.Driver != null || HasContents(uid)))
        {
            return false;
        }

        return MetaData(uid).EntityPrototype?.ID is { } prototypeId && prototypeIds.Contains(prototypeId);
    }

    private string GetProtectedEntityReason(EntityUid uid)
    {
        if (HasComp<ActorComponent>(uid))
            return "actor";
        if (HasComp<MobStateComponent>(uid))
            return "mob";
        if (HasComp<AggressiveSpaceJanitorExemptComponent>(uid))
            return "exempt";
        if (HasComp<DeletionCensusExemptComponent>(uid))
            return "deletion-census exempt";
        if (HasComp<MachineComponent>(uid) || HasComp<ComputerComponent>(uid))
            return "machine";
        return "map or grid";
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
