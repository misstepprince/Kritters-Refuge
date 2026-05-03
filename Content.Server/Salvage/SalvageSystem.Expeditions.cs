using System.Linq;
using System.Threading;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Content.Shared.Examine;
using Content.Shared.Random.Helpers;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Content.Server._NF.Salvage.Expeditions; // Frontier
using Content.Server.Station.Components; // Frontier
using Content.Shared.Procedural; // Frontier
using Content.Shared.Salvage; // Frontier
using Robust.Shared.Prototypes; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Shared.Shuttles.Components; // Frontier
using Robust.Shared.Configuration;
using Content.Shared.Ghost;
using System.Numerics; // Frontier

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles setup / teardown of salvage expeditions.
     */

    private const int MissionLimit = 5; // Frontier: 3<5

    private readonly JobQueue _salvageQueue = new();
    private readonly List<(SpawnSalvageMissionJob Job, CancellationTokenSource CancelToken)> _salvageJobs = new();
    private const double SalvageJobTime = 0.002;
    private readonly List<(ProtoId<SalvageDifficultyPrototype> id, int value)> _missionDifficulties = [("NFModerate", 0), ("NFHazardous", 1), ("NFExtreme", 2)]; // Frontier: mission difficulties with order

    [Dependency] private readonly IConfigurationManager _cfgManager = default!; // Frontier

    private float _cooldown;
    private float _failedCooldown; // Frontier
    public float TravelTime { get; private set; } // Frontier
    public bool ProximityCheck { get; private set; } // Frontier

    private void InitializeExpeditions()
    {
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ComponentInit>(OnSalvageConsoleInit);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, EntParentChangedMessage>(OnSalvageConsoleParent);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ClaimSalvageMessage>(OnSalvageClaimMessage);
        SubscribeLocalEvent<ExpeditionSpawnCompleteEvent>(OnExpeditionSpawnComplete); // Frontier: more gracefully handle expedition generation failures
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, FinishSalvageMessage>(OnSalvageFinishMessage); // Frontier: For early finish

        SubscribeLocalEvent<SalvageExpeditionComponent, MapInitEvent>(OnExpeditionMapInit);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentGetState>(OnExpeditionGetState);
        SubscribeLocalEvent<SalvageExpeditionComponent, EntityTerminatingEvent>(OnMapTerminating); // Frontier

        SubscribeLocalEvent<SalvageStructureComponent, ExaminedEvent>(OnStructureExamine);

        _cooldown = _cfgManager.GetCVar(CCVars.SalvageExpeditionCooldown);
        Subs.CVar(_cfgManager, CCVars.SalvageExpeditionCooldown, SetCooldownChange);
        _failedCooldown = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionFailedCooldown); // Frontier
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionFailedCooldown, SetFailedCooldownChange); // Frontier
        TravelTime = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionTravelTime); // Frontier
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionTravelTime, SetTravelTime); // Frontier
        ProximityCheck = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionProximityCheck); // Frontier
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionProximityCheck, SetProximityCheck); // Frontier
    }

    private void OnExpeditionGetState(EntityUid uid, SalvageExpeditionComponent component, ref ComponentGetState args)
    {
        args.State = new SalvageExpeditionComponentState()
        {
            Stage = component.Stage,
            SelectedSong = component.SelectedSong // Frontier: note, not dirtied on map init (not needed)
        };
    }

    private void SetCooldownChange(float obj)
    {
        // Update the active cooldowns if we change it.
        var diff = obj - _cooldown;

        foreach (var board in _sharedExpeditionBoards.Values)
        {
            board.NextOffer += TimeSpan.FromSeconds(diff);
            board.CooldownTime = TimeSpan.FromSeconds(obj);
            UpdateEconomyConsoles(board.EconomyId);
        }

        _cooldown = obj;
    }

    // Frontier: failed cooldowns
    private void SetFailedCooldownChange(float obj)
    {
        // Note: we don't know whether or not players have failed missions, so let's not punish/reward them if this gets changed.
        _failedCooldown = obj;
    }

    private void SetTravelTime(float obj)
    {
        TravelTime = obj;
    }

    private void SetProximityCheck(bool obj)
    {
        ProximityCheck = obj;
    }
    // End Frontier

    private void OnExpeditionMapInit(EntityUid uid, SalvageExpeditionComponent component, MapInitEvent args)
    {
        // Ensure any old pause cache for a reused map UID cannot affect a fresh expedition.
        _pausedExpeditionRemaining.Remove(uid);
        component.SelectedSong = _audio.ResolveSound(component.Sound);
    }

    private void OnExpeditionShutdown(EntityUid uid, SalvageExpeditionComponent component, ComponentShutdown args)
    {
        // Drop pause cache when expedition map is shutting down.
        _pausedExpeditionRemaining.Remove(uid);

        // component.Stream = _audio.Stop(component.Stream); // Frontier: moved to client

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            if (job.Station == component.Station)
            {
                cancelToken.Cancel();
                _salvageJobs.Remove((job, cancelToken));
            }
        }

        if (Deleted(component.Station))
            return;

        // Finish mission
        if (TryComp<SalvageExpeditionDataComponent>(component.Station, out var data))
        {
            FinishExpedition((component.Station, data), component, uid); // Frontier: add component
        }
    }

    private void UpdateExpeditions()
    {
        var currentTime = _timing.CurTime;
        _salvageQueue.Process();

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            switch (job.Status)
            {
                case JobStatus.Finished:
                    _salvageJobs.Remove((job, cancelToken));
                    break;
            }
        }

        foreach (var board in _sharedExpeditionBoards.Values)
        {
            if (board.NextOffer > currentTime)
                continue;

            board.Cooldown = false;
            board.NextOffer = currentTime + TimeSpan.FromSeconds(_cooldown);
            board.CooldownTime = TimeSpan.FromSeconds(_cooldown);
            board.ActiveMission = 0;
            board.JoinableExpedition = null;
            ClearPendingClaims(board);
            GenerateMissions(board);
            UpdateEconomyConsoles(board.EconomyId);
        }
    }

    private void FinishExpedition(Entity<SalvageExpeditionDataComponent> expedition, SalvageExpeditionComponent expeditionComp, EntityUid uid)
    {
        var announcement = expeditionComp.Completed
            ? Loc.GetString("salvage-expedition-completed")
            : Loc.GetString("salvage-expedition-failed");

        var participantCooldownSecs = expeditionComp.Completed ? _cooldown : _failedCooldown;
        foreach (var participant in expeditionComp.ParticipantStations)
        {
            if (!TryComp<SalvageExpeditionDataComponent>(participant, out var data))
                continue;

            data.ActiveMission = 0;
            data.CanFinish = false;
            data.Cooldown = true;
            data.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(participantCooldownSecs);
            data.CooldownTime = TimeSpan.FromSeconds(participantCooldownSecs);
            UpdateStationConsoles(participant);
        }

        if (_sharedExpeditionBoards.TryGetValue(expeditionComp.EconomyId, out var board) &&
            board.JoinableExpedition == uid)
        {
            board.JoinableExpedition = null;
            if (board.ActiveMission == expeditionComp.MissionParams.Index)
                board.ActiveMission = 0;

            board.Cooldown = true;
            board.Missions.Clear();
            var boardCooldownSecs = expeditionComp.Completed ? _cooldown : _failedCooldown;
            board.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(boardCooldownSecs);
            board.CooldownTime = TimeSpan.FromSeconds(boardCooldownSecs);
            ClearPendingClaims(board);
            UpdateEconomyConsoles(board.EconomyId);
        }

        expedition.Comp.ActiveMission = 0;
        expedition.Comp.CanFinish = false;
        // Frontier: separate timeout/announcement for success/failures
        if (expeditionComp.Completed)
        {
            expedition.Comp.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
            expedition.Comp.CooldownTime = TimeSpan.FromSeconds(_cooldown);
        }
        else
        {
            expedition.Comp.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_failedCooldown);
            expedition.Comp.CooldownTime = TimeSpan.FromSeconds(_failedCooldown);
        }
        // End Frontier: separate timeout/announcement for success/failures
        expedition.Comp.Cooldown = true;
        UpdateConsoles(expedition);
        Announce(uid, announcement);
    }

    private void GenerateMissions(SalvageExpeditionDataComponent component)
    {
        component.Missions.Clear();

        // Frontier: generate missions from an arbitrary set of difficulties
        if (_missionDifficulties.Count <= 0)
        {
            Log.Error("No expedition mission difficulties to pick from!");
            return;
        }

        // this doesn't support having more missions than types of ratings
        // but the previous system didn't do that either.
        var allDifficulties = _missionDifficulties; // Frontier: Enum.GetValues<DifficultyRating>() < _missionDifficulties
        _random.Shuffle(allDifficulties);
        var difficulties = allDifficulties.Take(MissionLimit).ToList();

        // If we support more missions than there are accepted types, pick more until you're up to MissionLimit
        while (difficulties.Count < MissionLimit)
        {
            var difficultyIndex = _random.Next(_missionDifficulties.Count);
            difficulties.Add(_missionDifficulties[difficultyIndex]);
        }
        difficulties.Sort((x, y) => { return Comparer<int>.Default.Compare(x.value, y.value); });

        for (var i = 0; i < MissionLimit; i++)
        {
            var mission = new SalvageMissionParams
            {
                Index = component.NextIndex,
                MissionType = (SalvageMissionType)_random.NextByte((byte)SalvageMissionType.Max + 1), // Frontier
                Seed = _random.Next(),
                Difficulty = difficulties[i].id,
            };

            component.Missions[component.NextIndex++] = mission;
        }
        // End Frontier: generate missions from an arbitrary set of difficulties
    }

    private void GenerateMissions(SharedExpeditionBoard board)
    {
        board.Missions.Clear();

        if (_missionDifficulties.Count <= 0)
        {
            Log.Error("No expedition mission difficulties to pick from!");
            return;
        }

        var allDifficulties = _missionDifficulties.ToList();
        _random.Shuffle(allDifficulties);
        var difficulties = allDifficulties.Take(MissionLimit).ToList();

        while (difficulties.Count < MissionLimit)
        {
            var difficultyIndex = _random.Next(_missionDifficulties.Count);
            difficulties.Add(_missionDifficulties[difficultyIndex]);
        }

        difficulties.Sort((x, y) => Comparer<int>.Default.Compare(x.value, y.value));

        for (var i = 0; i < MissionLimit; i++)
        {
            var mission = new SalvageMissionParams
            {
                Index = board.NextIndex,
                MissionType = (SalvageMissionType) _random.NextByte((byte) SalvageMissionType.Max + 1),
                Seed = _random.Next(),
                Difficulty = difficulties[i].id,
            };

            board.Missions[board.NextIndex++] = mission;
        }
    }

    private void SpawnMission(SalvageMissionParams missionParams, EntityUid station, EntityUid? coordinatesDisk, string economyId)
    {
        var cancelToken = new CancellationTokenSource();
        var job = new SpawnSalvageMissionJob(
            SalvageJobTime,
            EntityManager,
            _timing,
            _logManager,
            _prototypeManager,
            _anchorable,
            _biome,
            _dungeon,
            _metaData,
            _mapSystem,
            _station, // Frontier
            _shuttle, // Frontier
            this, // Frontier
            station,
            coordinatesDisk,
            economyId,
            missionParams,
            cancelToken.Token);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);
    }

    private void OnStructureExamine(EntityUid uid, SalvageStructureComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("salvage-expedition-structure-examine"));
    }

    // Frontier: exped job handling, ghost reparenting
    // Handle exped spawn job failures gracefully - reset the console
    private void OnExpeditionSpawnComplete(ExpeditionSpawnCompleteEvent ev)
    {
        if (!_sharedExpeditionBoards.TryGetValue(ev.EconomyId, out var board))
            return;

        if (!ev.Success)
        {
            board.ActiveMission = 0;
            board.JoinableExpedition = null;
            ClearPendingClaims(board);
            if (TryComp<SalvageExpeditionDataComponent>(ev.Station, out var stationData))
            {
                stationData.ActiveMission = 0;
                stationData.Cooldown = false;
                UpdateConsoles((ev.Station, stationData));
            }
            UpdateEconomyConsoles(board.EconomyId);
            return;
        }

        if (board.ActiveMission != ev.MissionIndex || !ev.MapUid.IsValid())
            return;

        board.JoinableExpedition = ev.MapUid;

        if (!TryComp<SalvageExpeditionComponent>(ev.MapUid, out var expedition))
        {
            UpdateEconomyConsoles(board.EconomyId);
            return;
        }

        foreach (var pending in board.PendingClaims.ToArray())
        {
            if (TryJoinExistingExpedition(board, pending.Station, pending.ConsoleUid, ev.MapUid, expedition))
                continue;

            if (!TryComp<SalvageExpeditionDataComponent>(pending.Station, out var pendingData))
                continue;

            pendingData.ActiveMission = 0;
            pendingData.CanFinish = false;

            if (EntityManager.EntityExists(pending.ConsoleUid))
            {
                PlayDenySound((pending.ConsoleUid, Comp<SalvageExpeditionConsoleComponent>(pending.ConsoleUid)));
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-no-valid-landing-zone"), pending.ConsoleUid, Content.Shared.Popups.PopupType.MediumCaution);
            }

            UpdateStationConsoles(pending.Station);
        }

        board.PendingClaims.Clear();
        UpdateEconomyConsoles(board.EconomyId);
    }

    // Send all ghosts (relevant for admins) back to the default map so they don't lose their stuff.
    private void OnMapTerminating(EntityUid uid, SalvageExpeditionComponent component, EntityTerminatingEvent ev)
    {
        var ghosts = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        var newCoords = new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);
        while (ghosts.MoveNext(out var ghostUid, out _, out var xform))
        {
            if (xform.MapUid == uid)
                _transform.SetMapCoordinates(ghostUid, newCoords);
        }
    }
    // End Frontier
}
