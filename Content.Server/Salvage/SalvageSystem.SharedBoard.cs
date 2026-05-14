using System.Numerics;
using System.Linq;
// _CS Start: shared landing atmosphere exclusions
using Content.Server._NF.Salvage.Expeditions;
// _CS End: shared landing atmosphere exclusions
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    private static readonly ProtoId<SalvageDifficultyPrototype> SharedOpenContractDifficulty = "NFExtreme";

    private sealed class SharedExpeditionBoard
    {
        public string EconomyId = string.Empty;
        public bool Cooldown;
        public TimeSpan NextOffer;
        public TimeSpan CooldownTime = TimeSpan.FromSeconds(1);
        public readonly Dictionary<ushort, SalvageMissionParams> Missions = new();
        public ushort NextIndex = 1;
        public ushort ActiveMission;
        public EntityUid? JoinableExpedition;
        public readonly List<PendingExpeditionClaim> PendingClaims = new();
    }

    private readonly record struct PendingExpeditionClaim(EntityUid Station, EntityUid ConsoleUid, ushort MissionIndex);

    private readonly Dictionary<string, SharedExpeditionBoard> _sharedExpeditionBoards = new();

    private SharedExpeditionBoard EnsureBoard(string economyId)
    {
        if (_sharedExpeditionBoards.TryGetValue(economyId, out var board))
            return board;

        board = new SharedExpeditionBoard
        {
            EconomyId = economyId,
            NextOffer = TimeSpan.Zero,
            Cooldown = false,
        };

        _sharedExpeditionBoards[economyId] = board;
        EnsureBoardReady(board);
        return board;
    }

    private void EnsureBoardReady(SharedExpeditionBoard board)
    {
        if (board.Missions.Count > 0 || board.NextOffer > _timing.CurTime)
            return;

        board.Cooldown = false;
        board.CooldownTime = TimeSpan.FromSeconds(SharedExpeditionCooldown);
        board.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(SharedExpeditionCooldown);
        board.ActiveMission = 0;
        board.JoinableExpedition = null;
        ClearPendingClaims(board);
        GenerateMissions(board);
    }

    private void ClearPendingClaims(SharedExpeditionBoard board)
    {
        foreach (var pending in board.PendingClaims)
        {
            if (!TryComp<SalvageExpeditionDataComponent>(pending.Station, out var pendingData))
                continue;

            pendingData.ActiveMission = 0;
            pendingData.CanFinish = false;
            UpdateStationConsoles(pending.Station);
        }

        board.PendingClaims.Clear();
    }

    private void UpdateStationConsoles(EntityUid station)
    {
        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var uiComp, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            UpdateConsole((uid, Comp<SalvageExpeditionConsoleComponent>(uid)), uiComp);
        }
    }

    private void UpdateEconomyConsoles(string economyId)
    {
        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var console, out var uiComp))
        {
            if (!string.Equals(console.EconomyId, economyId, StringComparison.Ordinal))
                continue;

            UpdateConsole((uid, console), uiComp);
        }
    }

    private SalvageExpeditionConsoleState GetState(Entity<SalvageExpeditionConsoleComponent> console, EntityUid? station, SalvageExpeditionDataComponent? stationData)
    {
        var board = EnsureBoard(console.Comp.EconomyId);

        var missions = new List<SalvageMissionParams>();

        if (stationData != null)
        {
            ApplySharedMissionOffer(board, stationData.Missions);
            missions.AddRange(stationData.Missions.Values);
        }

        return new SalvageExpeditionConsoleState(
            stationData?.NextOffer ?? board.NextOffer,
            stationData?.Claimed ?? false,
            stationData?.Cooldown ?? board.Cooldown,
            stationData?.ActiveMission ?? board.ActiveMission,
            missions,
            stationData?.CanFinish ?? false,
            stationData?.CooldownTime ?? board.CooldownTime,
            board.NextOffer,
            board.CooldownTime,
            board.Cooldown,
            false); // Frontier: shared board cooldown independent from private board
    }

    private static void ApplySharedMissionOffer(SharedExpeditionBoard board, Dictionary<ushort, SalvageMissionParams> stationMissions)
    {
        // Sentinel index reserved for the shared open contract slot.
        const ushort SharedSlotIndex = ushort.MaxValue;

        var sharedMission = GetSharedMissionOffer(board);

        if (sharedMission == null)
        {
            stationMissions.Remove(SharedSlotIndex);
            return;
        }

        stationMissions[SharedSlotIndex] = sharedMission with
        {
            Index = SharedSlotIndex,
            OpenContract = true,
            SharedMissionIndex = sharedMission.Index,
            Difficulty = SharedOpenContractDifficulty,
        };
    }

    private static SalvageMissionParams? GetSharedMissionOffer(SharedExpeditionBoard board)
    {
        if (board.ActiveMission != 0 && board.Missions.ContainsKey(board.ActiveMission))
        {
            var activeMission = board.Missions[board.ActiveMission];
            if (activeMission != null)
                return activeMission;
        }

        if (board.Missions.Count == 0)
            return null;

        return board.Missions.Values.MinBy(m => m.Index);
    }

    private bool TryGetStationShuttle(EntityUid station, out EntityUid shuttleUid, out MapGridComponent gridComp, out ShuttleComponent shuttleComp)
    {
        shuttleUid = EntityUid.Invalid;
        gridComp = default!;
        shuttleComp = default!;

        if (!TryComp<StationDataComponent>(station, out StationDataComponent? stationData) || stationData == null)
        {
            return false;
        }

        var grid = _station.GetLargestGrid(stationData);
        if (grid is not { Valid: true } shuttleGrid)
            return false;

        if (!TryComp<MapGridComponent>(shuttleGrid, out MapGridComponent? foundGrid) || foundGrid == null ||
            !TryComp<ShuttleComponent>(shuttleGrid, out ShuttleComponent? foundShuttle) || foundShuttle == null)
        {
            return false;
        }

        gridComp = foundGrid;
        shuttleComp = foundShuttle;

        shuttleUid = shuttleGrid;
        return true;
    }

    private bool HasBlockingLandingZone(EntityUid expeditionMap, Box2 candidateBox)
    {
        var shuttleQuery = AllEntityQuery<ShuttleComponent, MapGridComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out var shuttleUid, out _, out var grid, out var xform))
        {
            if (xform.MapUid != expeditionMap)
                continue;

            var shuttleBox = _transform.GetWorldMatrix(shuttleUid).TransformBox(grid.LocalAABB);
            if (shuttleBox.Intersects(candidateBox))
                return true;
        }

        foreach (var entity in _lookup.GetEntitiesIntersecting(expeditionMap, candidateBox.Enlarged(-0.1f)))
        {
            if (entity == expeditionMap || HasComp<ShuttleComponent>(entity) || HasComp<MapGridComponent>(entity))
                continue;

            var xform = Transform(entity);
            if (!xform.Anchored)
                continue;

            return true;
        }

        return false;
    }

    private static bool IntersectsReservedDungeonTiles(SalvageExpeditionComponent expedition, Box2 area, float minimumClearance)
    {
        var checkArea = area.Enlarged(minimumClearance);
        var minX = (int) MathF.Floor(checkArea.Left);
        var minY = (int) MathF.Floor(checkArea.Bottom);
        var maxX = (int) MathF.Ceiling(checkArea.Right) - 1;
        var maxY = (int) MathF.Ceiling(checkArea.Top) - 1;

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (!expedition.ReservedTiles.Contains(new Vector2i(x, y)))
                    continue;

                var tileMin = new Vector2(x, y);
                var tileBox = new Box2(tileMin, tileMin + Vector2.One);
                if (SalvageExpeditionReservation.IsWithinClearance(area, tileBox, minimumClearance))
                    return true;
            }
        }

        return false;
    }

    private bool TryFindLandingOrigin(EntityUid expeditionMap, SalvageExpeditionComponent expedition, Box2 shuttleBox, out Vector2 origin)
    {
        // _CS Start: shared expedition landing separation
        // Shared expeditions keep claimed ships farther apart than the normal tile-reservation fallback.
        const float sharedLandingSeparationTiles = 32f;
        var dungeonBuffer = sharedLandingSeparationTiles;
        // _CS End: shared expedition landing separation
        var boardPadding = MathF.Max(shuttleBox.Width, shuttleBox.Height) + 24f;
        var searchRadius = expedition.SharedLandingRadius > 0f
            ? expedition.SharedLandingRadius
            : boardPadding;
        var center = expedition.DungeonLocation != Vector2.Zero
            ? expedition.DungeonLocation
            : expedition.DungeonBounds.Center;

        var baseAngle = expedition.SharedLandingRadius > 0f
            ? expedition.SharedLandingAngle + MathF.PI
            : 0f;

        ReadOnlySpan<float> angleOffsets =
        [
            0f,
            MathF.PI / 8f,
            -MathF.PI / 8f,
            MathF.PI / 4f,
            -MathF.PI / 4f,
            3f * MathF.PI / 8f,
            -3f * MathF.PI / 8f,
            MathF.PI / 2f,
            -MathF.PI / 2f,
        ];

        for (var ring = 0; ring < 8; ring++)
        {
            var radius = searchRadius + ring * 4f;
            for (var step = 0; step < angleOffsets.Length; step++)
            {
                var theta = baseAngle + angleOffsets[step];
                var candidateCenter = center + new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * radius;
                var candidateOrigin = (candidateCenter - shuttleBox.Center).Rounded();
                var candidateShuttleArea = SalvageExpeditionReservation.GetShuttleFootprint(shuttleBox, candidateOrigin);
                var candidateReservationZone = SalvageExpeditionReservation.GetLandingZone(shuttleBox, candidateOrigin, dungeonBuffer);

                if (IntersectsReservedDungeonTiles(expedition, candidateShuttleArea, dungeonBuffer))
                    continue;

                if (SalvageExpeditionReservation.IntersectsReservedLandingZone(expedition, candidateShuttleArea, dungeonBuffer))
                    continue;

                if (HasBlockingLandingZone(expeditionMap, candidateShuttleArea))
                    continue;

                origin = candidateOrigin;
                expedition.ReservedLandingZones.Add(candidateReservationZone);

                // _CS Start: keep claim-landed ships clear of expedition map atmosphere
                if (TryComp<ExpeditionAtmosphereExclusionComponent>(expeditionMap, out var atmosExclusion))
                    atmosExclusion.ExcludedZones.Add(candidateReservationZone);
                // _CS End: keep claim-landed ships clear of expedition map atmosphere

                return true;
            }
        }

        origin = Vector2.Zero;
        return false;
    }

    private bool TryJoinExistingExpedition(SharedExpeditionBoard board, EntityUid station, EntityUid consoleUid, ushort stationMissionIndex, EntityUid expeditionMap, SalvageExpeditionComponent expedition)
    {
        if (!TryGetStationShuttle(station, out var shuttleUid, out var shuttleGrid, out var shuttleComp))
            return false;

        if (!TryFindLandingOrigin(expeditionMap, expedition, shuttleGrid.LocalAABB, out var landingOrigin))
            return false;

        expedition.ParticipantStations.Add(station);
        Dirty(expeditionMap, expedition);

        if (TryComp<SalvageExpeditionDataComponent>(station, out var data))
        {
            data.ActiveMission = stationMissionIndex;
            data.CanFinish = false;
        }

        _shuttle.FTLToCoordinates(shuttleUid, shuttleComp, new Robust.Shared.Map.EntityCoordinates(expeditionMap, landingOrigin), Angle.Zero, 5.5f, TravelTime);
        UpdateStationConsoles(station);
        return true;
    }
}