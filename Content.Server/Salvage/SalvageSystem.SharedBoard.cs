using System.Numerics;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
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

    private readonly record struct PendingExpeditionClaim(EntityUid Station, EntityUid ConsoleUid);

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
        board.CooldownTime = TimeSpan.FromSeconds(_cooldown);
        board.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
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
        var missions = new List<SalvageMissionParams>(board.Missions.Values);
        return new SalvageExpeditionConsoleState(
            board.NextOffer,
            stationData?.Claimed ?? false,
            board.Cooldown,
            board.ActiveMission,
            missions,
            stationData?.CanFinish ?? false,
            board.CooldownTime);
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

    private static Box2 GetLandingZone(Box2 shuttleBox, Vector2 origin, float padding = 16f)
    {
        return shuttleBox.Translated(origin).Enlarged(padding);
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

    private bool TryFindLandingOrigin(EntityUid expeditionMap, SalvageExpeditionComponent expedition, Box2 shuttleBox, out Vector2 origin)
    {
        var exclusion = expedition.DungeonBounds.Enlarged(12f);
        var boardPadding = MathF.Max(shuttleBox.Width, shuttleBox.Height) + 24f;
        var searchRadius = MathF.Max(exclusion.Width, exclusion.Height) / 2f + boardPadding;
        var center = exclusion.Center;

        for (var ring = 0; ring < 8; ring++)
        {
            var radius = searchRadius + ring * (boardPadding * 0.75f);
            for (var step = 0; step < 16; step++)
            {
                var theta = MathF.Tau * step / 16f;
                var candidateCenter = center + new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * radius;
                var candidateOrigin = (candidateCenter - shuttleBox.Center).Rounded();
                var candidateBox = GetLandingZone(shuttleBox, candidateOrigin);

                if (candidateBox.Intersects(exclusion))
                    continue;

                var intersectsReserved = false;
                foreach (var zone in expedition.ReservedLandingZones)
                {
                    if (!zone.Intersects(candidateBox))
                        continue;

                    intersectsReserved = true;
                    break;
                }

                if (intersectsReserved)
                    continue;

                if (HasBlockingLandingZone(expeditionMap, candidateBox))
                    continue;

                origin = candidateOrigin;
                expedition.ReservedLandingZones.Add(candidateBox);
                return true;
            }
        }

        origin = Vector2.Zero;
        return false;
    }

    private bool TryJoinExistingExpedition(SharedExpeditionBoard board, EntityUid station, EntityUid consoleUid, EntityUid expeditionMap, SalvageExpeditionComponent expedition)
    {
        if (!TryGetStationShuttle(station, out var shuttleUid, out var shuttleGrid, out var shuttleComp))
            return false;

        if (!TryFindLandingOrigin(expeditionMap, expedition, shuttleGrid.LocalAABB, out var landingOrigin))
            return false;

        expedition.ParticipantStations.Add(station);
        Dirty(expeditionMap, expedition);

        if (TryComp<SalvageExpeditionDataComponent>(station, out var data))
        {
            data.ActiveMission = board.ActiveMission;
            data.CanFinish = false;
        }

        _shuttle.FTLToCoordinates(shuttleUid, shuttleComp, new Robust.Shared.Map.EntityCoordinates(expeditionMap, landingOrigin), Angle.Zero, 5.5f, TravelTime);
        UpdateStationConsoles(station);
        return true;
    }
}