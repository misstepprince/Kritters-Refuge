using Content.Shared.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NF.Salvage.Expeditions;

/// <summary>
/// Drives <see cref="SalvageObjectiveNpcSpawnerComponent"/> objective structure spawning.
/// </summary>
public sealed class SalvageObjectiveNpcSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SalvageObjectiveNpcSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SalvageObjectiveNpcSpawnerComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (Paused(uid) || comp.SpawnPrototypes.Count == 0 || comp.NextSpawn > now)
                continue;

            comp.NextSpawn += TimeSpan.FromSeconds(comp.SpawnIntervalSeconds);

            if (CountNearbyFactionMobs(uid, comp) >= comp.MaxNearby)
                continue;

            var spawn = _random.Pick(comp.SpawnPrototypes);
            SpawnAtPosition(spawn, Transform(uid).Coordinates);
        }
    }

    private void OnMapInit(Entity<SalvageObjectiveNpcSpawnerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextSpawn = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.SpawnIntervalSeconds);
    }

    private int CountNearbyFactionMobs(EntityUid uid, SalvageObjectiveNpcSpawnerComponent comp)
    {
        var xform = Transform(uid);
        var mapCoords = _xforms.GetMapCoordinates((uid, xform));
        var count = 0;

        foreach (var nearby in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(mapCoords, comp.NearbyRange))
        {
            if (nearby.Owner == uid)
                continue;

            if (!comp.NearbyFactions.Overlaps(nearby.Comp.Factions))
                continue;

            count++;
        }

        return count;
    }
}
