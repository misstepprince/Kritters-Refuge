using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Spawners.EntitySystems;

public sealed partial class SpawnPointSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // TODO: Cache all this if it ends up important.
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possiblePositions = new List<EntityCoordinates>();
        var fallbackPositions = new List<EntityCoordinates>();

        while ( points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (args.Station != null && _stationSystem.GetOwningStation(uid, xform) != args.Station)
                continue;

            // Delta-V: Allow setting a desired SpawnPointType
            if (args.DesiredSpawnPointType != SpawnPointType.Unset)
            {
                var isMatchingJob = spawnPoint.SpawnType == SpawnPointType.Job && (args.Job == null || spawnPoint.Job == args.Job);
                var isMatchingAltJob = spawnPoint.SpawnType == SpawnPointType.Job && (args.Job != null && spawnPoint.AltJobs.Contains(args.Job.Value));

                switch (args.DesiredSpawnPointType)
                {
                    case SpawnPointType.Job when isMatchingJob:
                    case SpawnPointType.Job when isMatchingAltJob:
                    case SpawnPointType.LateJoin when spawnPoint.SpawnType == SpawnPointType.LateJoin:
                    case SpawnPointType.Observer when spawnPoint.SpawnType == SpawnPointType.Observer:
                        if (isMatchingJob)
                            possiblePositions.Add(xform.Coordinates);
                        else if (isMatchingAltJob)
                            fallbackPositions.Add(xform.Coordinates);
                        else
                            possiblePositions.Add(xform.Coordinates);
                        break;
                    default:
                        continue;
                }
            }

            if (_gameTicker.RunLevel == GameRunLevel.InRound && spawnPoint.SpawnType == SpawnPointType.LateJoin)
            {
                possiblePositions.Add(xform.Coordinates);
            }

            if (_gameTicker.RunLevel != GameRunLevel.InRound
                && spawnPoint.SpawnType == SpawnPointType.Job)
            {
                if (args.Job == null || spawnPoint.Job == args.Job)
                {
                    possiblePositions.Add(xform.Coordinates);
                }
                else if (spawnPoint.AltJobs.Contains(args.Job.Value))
                {
                    fallbackPositions.Add(xform.Coordinates);
                }
            }
        }

        if (possiblePositions.Count == 0)
        {
            if (fallbackPositions.Count > 0)
            {
                possiblePositions.AddRange(fallbackPositions);
            }
            else
            {
                // Ok we've still not returned, but we need to put them /somewhere/.
                // TODO: Refactor gameticker spawning code so we don't have to do this!
                var points2 = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();

                if (points2.MoveNext(out var spawnPoint, out var xform))
                {
                    possiblePositions.Add(xform.Coordinates);
                }
                else
                {
                    Log.Error("No spawn points were available!");
                    return;
                }
            }
        }

        var spawnLoc = _random.Pick(possiblePositions);
        if (spawnLoc == null && fallbackPositions.Count > 0)
        {
            spawnLoc = _random.Pick(fallbackPositions);
        }

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station,
            session: args.Session); // Frontier
    }
}
