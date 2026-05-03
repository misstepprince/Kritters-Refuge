using Content.Shared.Shuttles.Components;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Content.Shared.Popups; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Server.Station.Components; // Frontier
using Robust.Shared.Map.Components; // Frontier
using Robust.Shared.Physics.Components; // Frontier
using Content.Shared.NPC; // Frontier
using Content.Server._NF.Salvage; // Frontier
using Content.Shared.NPC.Components; // Frontier
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Shared.Mind.Components; // Frontier
using Content.Shared.Mobs.Components; // Frontier
using Robust.Shared.Physics; // Frontier
using System.Collections.Generic;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    public static readonly string CoordinatesDisk = "CoordinatesDisk";

    [Dependency] private readonly SharedPopupSystem _popupSystem = default!; // Frontier

    private const float ShuttleFTLMassThreshold = 50f; // Frontier
    private const float ShuttleFTLRange = 150f; // Frontier

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        var station = _station.GetOwningStation(uid);

        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || data.Claimed)
            return;

        var board = EnsureBoard(component.EconomyId);
        if (!board.Missions.TryGetValue(args.Index, out var missionparams))
            return;

        if (board.ActiveMission != 0 && board.ActiveMission != args.Index)
            return;

        // Frontier: FTL travel is currently restricted to expeditions and such, and so we need to put this here
        #region Frontier FTL changes
        // until FTL changes for us in some way.

        // Run a proximity check (unless using a debug console)
        if (ProximityCheck && !component.Debug)
        {
            if (!TryComp<StationDataComponent>(station, out var stationData)
                || _station.GetLargestGrid(stationData) is not { Valid: true } ourGrid
                || !TryComp<MapGridComponent>(ourGrid, out var gridComp))
            {
                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-invalid"), uid, PopupType.MediumCaution);
                UpdateStationConsoles(station.Value);
                return;
            }

            if (HasComp<FTLComponent>(ourGrid))
            {
                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-recharge"), uid, PopupType.MediumCaution);
                UpdateStationConsoles(station.Value);
                return;
            }

            var xform = Transform(ourGrid);
            var bounds = _transform.GetWorldMatrix(ourGrid).TransformBox(gridComp.LocalAABB).Enlarged(ShuttleFTLRange);
            var bodyQuery = GetEntityQuery<PhysicsComponent>();
            var otherGrids = new List<Entity<MapGridComponent>>();
            _mapManager.FindGridsIntersecting(xform.MapID, bounds, ref otherGrids);
            foreach (var otherGrid in otherGrids)
            {
                if (ourGrid == otherGrid.Owner ||
                    !bodyQuery.TryGetComponent(otherGrid.Owner, out var body) ||
                    body.Mass < ShuttleFTLMassThreshold && body.BodyType == BodyType.Dynamic)
                {
                    continue;
                }

                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-proximity"), uid, PopupType.MediumCaution);
                UpdateStationConsoles(station.Value);
                return;
            }
        }
        #endregion Frontier FTL changes
        // End Frontier

        if (board.ActiveMission == 0)
        {
            // Frontier: prevent expeditions if there are too many out already.
            var activeExpeditionCount = 0;
            var expeditionQuery = AllEntityQuery<SalvageExpeditionComponent>();
            while (expeditionQuery.MoveNext(out _, out _))
            {
                activeExpeditionCount++;
            }

            if (activeExpeditionCount >= _cfgManager.GetCVar(NFCCVars.SalvageExpeditionMaxActive))
            {
                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-too-many"), uid, PopupType.MediumCaution);
                UpdateStationConsoles(station.Value);
                return;
            }

            board.ActiveMission = args.Index;
            data.ActiveMission = args.Index;
            SpawnMission(missionparams, station.Value, null, component.EconomyId);
            UpdateStationConsoles(station.Value);
            UpdateEconomyConsoles(component.EconomyId);
            return;
        }

        if (board.JoinableExpedition is not { Valid: true } expeditionMap ||
            !TryComp<SalvageExpeditionComponent>(expeditionMap, out var expedition))
        {
            var hasPendingClaim = false;
            foreach (var pending in board.PendingClaims)
            {
                if (pending.Station != station.Value)
                    continue;

                hasPendingClaim = true;
                break;
            }

            if (!hasPendingClaim)
                board.PendingClaims.Add(new PendingExpeditionClaim(station.Value, uid));

            data.ActiveMission = args.Index;
            UpdateStationConsoles(station.Value);
            UpdateEconomyConsoles(component.EconomyId);
            return;
        }

        if (!TryJoinExistingExpedition(board, station.Value, uid, expeditionMap, expedition))
        {
            PlayDenySound((uid, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-no-valid-landing-zone"), uid, PopupType.MediumCaution);
            UpdateStationConsoles(station.Value);
            return;
        }

        UpdateEconomyConsoles(component.EconomyId);
    }

    // Frontier: early expedition end
    private void OnSalvageFinishMessage(EntityUid entity, SalvageExpeditionConsoleComponent component, FinishSalvageMessage e)
    {
        var station = _station.GetOwningStation(entity);
        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || !data.CanFinish)
            return;

        // Based on SalvageSystem.Runner:OnConsoleFTLAttempt
        if (!TryComp(entity, out TransformComponent? xform)) // Get the console's grid (if you move it, rip you)
        {
            PlayDenySound((entity, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            UpdateConsoles((station.Value, data));
            return;
        }

        // Frontier: check if any player characters or friendly ghost roles are outside
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var _, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Not player controlled (ghosted)
            if (!mindContainer.HasMind)
                continue;

            // NPC, definitely not a person
            if (HasComp<ActiveNPCComponent>(uid) || HasComp<NFSalvageMobRestrictionsComponent>(uid))
                continue;

            // Hostile ghost role, continue
            if (TryComp(uid, out NpcFactionMemberComponent? npcFaction))
            {
                var hostileFactions = npcFaction.HostileFactions;
                if (hostileFactions.Contains("NanoTrasen")) // TODO: move away from hardcoded faction
                    continue;
            }

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != xform.GridUid)
            {
                PlayDenySound((entity, component));
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-not-everyone-aboard", ("target", uid)), entity, PopupType.MediumCaution);
                UpdateConsoles((station.Value, data));
                return;
            }
        }
        // End SalvageSystem.Runner:OnConsoleFTLAttempt

        data.CanFinish = false;
        UpdateConsoles((station.Value, data));

        var map = Transform(entity).MapUid;

        if (!TryComp<SalvageExpeditionComponent>(map, out var expedition))
            return;

        const int departTime = 20;
        var newEndTime = _timing.CurTime + TimeSpan.FromSeconds(departTime);

        if (expedition.EndTime <= newEndTime)
            return;

        expedition.Stage = ExpeditionStage.FinalCountdown;
        expedition.EndTime = newEndTime;
        Dirty(map.Value, expedition);

        // Frontier: if an expedition-extending anchor is active, update the pause cache so it reflects
        // the new expedited departure time rather than the original remaining time. Without this, when
        // the anchor deactivates, the resume logic restores the old large remaining value, causing the
        // shuttle to behave like open space (Dynamic physics) on the expedition map for the full original
        // duration instead of departing after the requested 20-second countdown.
        _pausedExpeditionRemaining[map.Value] = TimeSpan.FromSeconds(departTime);
        // End Frontier

        Announce(map.Value, Loc.GetString("salvage-expedition-announcement-early-finish", ("departTime", departTime)));
    }
    // End Frontier: early expedition end

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(Entity<SalvageExpeditionDataComponent> component)
    {
        UpdateStationConsoles(component.Owner);
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        if (!TryComp<UserInterfaceComponent>(component.Owner, out var uiComp))
            return;

        UpdateConsole(component, uiComp);
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component, UserInterfaceComponent uiComp)
    {
        var station = _station.GetOwningStation(component);
        TryComp<SalvageExpeditionDataComponent>(station, out var dataComponent);
        var state = GetState(component, station, dataComponent);

        // Frontier: if we have a lingering FTL component, we cannot start a new mission
        if (station is not { Valid: true } ||
            !TryComp<StationDataComponent>(station.Value, out var stationData) ||
            _station.GetLargestGrid(stationData) is not { Valid: true } grid ||
            HasComp<FTLComponent>(grid))
        {
            state.Cooldown = true; //Hack: disable buttons
        }
        // End Frontier

        _ui.SetUiState((component.Owner, uiComp), SalvageConsoleUiKey.Expedition, state);
    }

    // Frontier: deny sound
    private void PlayDenySound(Entity<SalvageExpeditionConsoleComponent> ent)
    {
        _audio.PlayPvs(_audio.ResolveSound(ent.Comp.ErrorSound), ent);
    }
    // End Frontier
}
