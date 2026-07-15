using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Server.Station;
using Content.Server.StationRecords;
using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Power.Generator;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Light.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard;
using Content.Shared.Access.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.EntitySerialization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Serialization;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class BluespaceDrydockSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IResourceManager _resourceManager = default!;
    [Dependency] private GeneratorSystem _generator = default!;
    [Dependency] private PowerChargeSystem _powerCharge = default!;

    private ISawmill _sawmill = default!;

    // Delay between processing steps to reduce lag spikes
    private const float ProcessingDelaySeconds = 0.3f;

    // Track entities being processed to prevent duplicate operations
    private readonly HashSet<EntityUid> _processingStores = new();
    private readonly HashSet<EntityUid> _processingRetrieves = new();

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("drydock");

        SubscribeLocalEvent<BluespaceDrydockConsoleComponent, ComponentStartup>(OnConsoleStartup);
        SubscribeLocalEvent<BluespaceDrydockConsoleComponent, BoundUIOpenedEvent>(OnConsoleUIOpened);
        SubscribeLocalEvent<BluespaceDrydockConsoleComponent, BluespaceDrydockStoreMessage>(OnStoreMessage);
        SubscribeLocalEvent<BluespaceDrydockConsoleComponent, BluespaceDrydockRetrieveMessage>(OnRetrieveMessage);
        SubscribeLocalEvent<BluespaceDrydockConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<BluespaceDrydockConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
    }

    private void OnConsoleStartup(EntityUid uid, BluespaceDrydockConsoleComponent component, ComponentStartup args)
    {
    }

    private void OnConsoleUIOpened(EntityUid uid, BluespaceDrydockConsoleComponent component, BoundUIOpenedEvent args)
    {
        RefreshState(uid, component);
    }

    private void OnItemSlotChanged(EntityUid uid, BluespaceDrydockConsoleComponent component, ContainerModifiedMessage args)
    {
        RefreshState(uid, component);
    }

    private void OnStoreMessage(EntityUid consoleUid, BluespaceDrydockConsoleComponent component, BluespaceDrydockStoreMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Get the ID card from the console slot
        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            ConsolePopup(player, "No ID card in console!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Check if the ID card has an active ship deed
        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) || deed.ShuttleUid == null)
        {
            ConsolePopup(player, "No active ship found on ID card!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Check if there's already a stored ship
        if (HasComp<BluespaceStorageComponent>(targetId))
        {
            ConsolePopup(player, "ID card already has a stored ship!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        var shuttleUid = deed.ShuttleUid.Value;

        // Check if this shuttle is already being processed
        if (_processingStores.Contains(shuttleUid))
        {
            ConsolePopup(player, "Ship is already being stored, please wait...");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Check if there are any mobs/players on the ship
        var mobQuery = AllEntityQuery<MobStateComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var mobUid, out var _, out var mobXform))
        {
            if (mobXform.GridUid == shuttleUid)
            {
                ConsolePopup(player, "All crew must disembark before storing the ship!");
                PlayDenySound(player, consoleUid, component);
                return;
            }
        }

        // Check if the shuttle is docked
        if (!IsShuttleDocked(shuttleUid))
        {
            ConsolePopup(player, "Ship must be docked to store it!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Mark as processing
        _processingStores.Add(shuttleUid);

        // Start the multi-step storage process with delays
        ConsolePopup(player, "Beginning ship storage sequence...");

        // Step 1: Power down systems (delayed)
        Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
        {
            if (!Exists(shuttleUid) || !Exists(targetId))
            {
                _processingStores.Remove(shuttleUid);
                return;
            }

            PowerDownShipSystems(shuttleUid);
            ConsolePopup(player, "Ship systems powered down...");

            // Step 2: Delete station (delayed)
            Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
            {
                if (!Exists(shuttleUid) || !Exists(targetId))
                {
                    _processingStores.Remove(shuttleUid);
                    return;
                }

                if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStation)
                {
                    _sawmill.Info($"Deleting station {shuttleStation} before serializing ship {shuttleUid}");

                    _station.DeleteStation(shuttleStation);
                    _sawmill.Info($"Queued station {shuttleStation} for standard deletion lifecycle");
                }

                ConsolePopup(player, "Ship undocking...");

                // Step 3: Undock shuttle (delayed)
                Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                {
                    if (!Exists(shuttleUid) || !Exists(targetId))
                    {
                        _processingStores.Remove(shuttleUid);
                        return;
                    }

                    UndockShuttle(shuttleUid);
                    ConsolePopup(player, "Serializing ship data...");

                    // Step 4: Serialize asynchronously (heavy operation - don't block server)
                    var serializeTask = TrySerializeShuttleAsync(shuttleUid);
                    serializeTask.ContinueWith(task =>
                    {
                        if (!Exists(shuttleUid) || !Exists(targetId))
                        {
                            _processingStores.Remove(shuttleUid);
                            return;
                        }

#pragma warning disable RA0004
                        var (success, serializedData) = task.Result;
#pragma warning restore RA0004

                        if (!success || string.IsNullOrEmpty(serializedData))
                        {
                            ConsolePopup(player, "Failed to serialize ship data!");
                            PlayDenySound(player, consoleUid, component);
                            _processingStores.Remove(shuttleUid);
                            return;
                        }

                        _sawmill.Info($"Successfully serialized ship {shuttleUid}");

                        // Step 5: Finalize storage (delayed)
                        Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                        {
                            if (!Exists(targetId))
                            {
                                _processingStores.Remove(shuttleUid);
                                return;
                            }

                            // Get deed info before we delete the shuttle
                            var shuttleName = deed.ShuttleName;
                            var shuttleFullName = ShipyardSystem.GetFullName(deed);

                            // Store the ship data on the ID card
                            var storage = EnsureComp<BluespaceStorageComponent>(targetId);
                            storage.StoredGridData = serializedData;
                            storage.StoredShipName = shuttleName;
                            storage.StoredShipFullName = shuttleFullName;
                            storage.StoredTime = _timing.CurTime;
                            Dirty(targetId, storage);

                            // Delete the active ship deed and the shuttle
                            RemComp<ShuttleDeedComponent>(targetId);
                            QueueDel(shuttleUid);

                            ConsolePopup(player, $"Ship '{shuttleFullName}' stored successfully!");
                            PlayConfirmSound(player, consoleUid, component);
                            RefreshState(consoleUid, component);

                            _processingStores.Remove(shuttleUid);
                            _sawmill.Info($"{ToPrettyString(player)} stored ship {shuttleUid} on ID {ToPrettyString(targetId)}");
                        });
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                });
            });
        });
    }

    private void OnRetrieveMessage(EntityUid consoleUid, BluespaceDrydockConsoleComponent component, BluespaceDrydockRetrieveMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Get the ID card from the console slot
        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            ConsolePopup(player, "No ID card in console!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Check if the ID card already has an active ship
        if (HasComp<ShuttleDeedComponent>(targetId))
        {
            ConsolePopup(player, "ID card already has an active ship!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Check if there's a stored ship
        if (!TryComp<BluespaceStorageComponent>(targetId, out var storage) ||
            string.IsNullOrEmpty(storage.StoredGridData))
        {
            ConsolePopup(player, "No stored ship found on ID card!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Check if already processing this ID card
        if (_processingRetrieves.Contains(targetId))
        {
            ConsolePopup(player, "Ship is already being retrieved, please wait...");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Get the station to dock the ship to
        if (_station.GetOwningStation(consoleUid) is not { Valid: true } station)
        {
            ConsolePopup(player, "Console is not on a valid station!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        if (!TryComp<StationDataComponent>(station, out var stationData))
        {
            ConsolePopup(player, "Invalid station data!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        var targetGrid = _station.GetLargestGrid(stationData);
        if (targetGrid == null)
        {
            ConsolePopup(player, "Station has no valid grid!");
            PlayDenySound(player, consoleUid, component);
            return;
        }

        // Mark as processing
        _processingRetrieves.Add(targetId);

        // Cache storage data before we start
        var storedData = storage.StoredGridData;
        var storedShipName = storage.StoredShipName;
        var storedShipFullName = storage.StoredShipFullName;

        // Start the multi-step retrieval process with delays
        ConsolePopup(player, "Beginning ship retrieval sequence...");
        ConsolePopup(player, "Materializing ship...");

        // Step 1: Deserialize asynchronously (heavy operation - don't block server)
        var deserializeTask = TryDeserializeShuttleAsync(storedData);
        deserializeTask.ContinueWith(task =>
        {
            if (!Exists(targetId))
            {
                _processingRetrieves.Remove(targetId);
                return;
            }

#pragma warning disable RA0004
            var shuttleUid = task.Result;
#pragma warning restore RA0004

            if (shuttleUid == null)
            {
                ConsolePopup(player, "Failed to retrieve ship from storage!");
                PlayDenySound(player, consoleUid, component);
                _processingRetrieves.Remove(targetId);
                return;
            }

            var shuttleUidValue = shuttleUid.Value;

            // Step 2: Create station (delayed)
            Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
            {
                if (!Exists(shuttleUidValue) || !Exists(targetId))
                {
                    _processingRetrieves.Remove(targetId);
                    return;
                }

                ConsolePopup(player, "Initializing ship systems...");

                EntityUid? shuttleStation = null;
                if (TryComp<ShuttleComponent>(shuttleUidValue, out var shuttleComp))
                {
                    shuttleComp.PlayerShuttle = true;

                    var stationConfig = new StationConfig
                    {
                        StationPrototype = "StandardFrontierVessel"
                    };
                    List<EntityUid> gridUids = new() { shuttleUidValue };
                    shuttleStation = _station.InitializeNewStation(stationConfig, gridUids);

                    if (shuttleStation != null)
                    {
                        _station.RenameStation(shuttleStation.Value, storedShipFullName ?? "Unknown Ship", loud: false);
                    }
                }

                // Step 3: Dock shuttle (delayed)
                Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                {
                    if (!Exists(shuttleUidValue) || !Exists(targetId))
                    {
                        _processingRetrieves.Remove(targetId);
                        return;
                    }

                    ConsolePopup(player, "Docking ship...");

                    if (shuttleComp != null)
                    {
                        _shuttle.TryFTLDock(shuttleUidValue, shuttleComp, targetGrid.Value);
                    }

                    // Step 4: Finalize (delayed)
                    Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                    {
                        if (!Exists(shuttleUidValue) || !Exists(targetId))
                        {
                            _processingRetrieves.Remove(targetId);
                            return;
                        }

                        // Restore the ship deed
                        var deed = EnsureComp<ShuttleDeedComponent>(targetId);
                        deed.ShuttleUid = shuttleUidValue;
                        deed.ShuttleName = storedShipName;
                        deed.ShuttleOwner = "Unknown";
                        Dirty(targetId, deed);

                        // Also add deed to the shuttle itself
                        var shuttleDeed = EnsureComp<ShuttleDeedComponent>(shuttleUidValue);
                        shuttleDeed.ShuttleUid = shuttleUidValue;
                        shuttleDeed.ShuttleName = storedShipName;
                        shuttleDeed.ShuttleOwner = "Unknown";
                        Dirty(shuttleUidValue, shuttleDeed);

                        // Clear the storage
                        RemComp<BluespaceStorageComponent>(targetId);

                        ConsolePopup(player, $"Ship '{storedShipFullName}' retrieved successfully!");
                        PlayConfirmSound(player, consoleUid, component);
                        RefreshState(consoleUid, component);

                        _processingRetrieves.Remove(targetId);
                        _sawmill.Info($"{ToPrettyString(player)} retrieved ship {shuttleUidValue} from ID {ToPrettyString(targetId)}");
                    });
                });
            });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool IsShuttleDocked(EntityUid shuttleUid)
    {
        // Check if any docking ports on the shuttle are docked
        var docks = _docking.GetDocks(shuttleUid);
        foreach (var dock in docks)
        {
            if (dock.Comp.DockedWith != null)
                return true;
        }
        return false;
    }

    private void UndockShuttle(EntityUid shuttleUid)
    {
        // Undock all docking ports on the shuttle
        var docks = _docking.GetDocks(shuttleUid);
        foreach (var dock in docks)
        {
            if (dock.Comp.DockedWith != null)
            {
                _docking.Undock(dock);
            }
        }
    }

    private async Task<(bool success, string? data)> TrySerializeShuttleAsync(EntityUid shuttleUid)
    {
        try
        {
            // Use a temporary path to save the grid
            var tempPath = new ResPath($"/drydock_temp_{shuttleUid}.yml");

            // First try with IncludeNullspace to preserve networks and item references
            var options = new SerializationOptions
            {
                MissingEntityBehaviour = MissingEntityBehaviour.IncludeNullspace,
                ErrorOnOrphan = false,
                LogAutoInclude = null
            };

            // Save the grid using MapLoaderSystem
            if (!_mapLoader.TrySaveGrid(shuttleUid, tempPath, options))
            {
                _sawmill.Warning($"Failed to save grid {shuttleUid} with IncludeNullspace, retrying with Ignore");

                // If that fails (likely due to station references), try with Ignore
                options = new SerializationOptions
                {
                    MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                    ErrorOnOrphan = false,
                    LogAutoInclude = null
                };

                if (!_mapLoader.TrySaveGrid(shuttleUid, tempPath, options))
                {
                    _sawmill.Error($"Failed to save grid {shuttleUid} even with Ignore mode");
                    return (false, null);
                }

                _sawmill.Warning($"Saved grid {shuttleUid} with Ignore mode - some item functionality may be lost");
            }

            // Read the saved file asynchronously to avoid blocking
            var userData = _resourceManager.UserData;
            string serializedData;

            // Ensure streams are disposed before deletion (Windows file handle issue)
            {
                using var stream = userData.OpenRead(tempPath);
                using var reader = new StreamReader(stream);
                serializedData = await reader.ReadToEndAsync();
            }

            // Clean up temp file - wrapped in try-catch for Windows file handle issues
            try
            {
                userData.Delete(tempPath);
            }
            catch (IOException ioEx)
            {
                _sawmill.Warning($"Failed to delete temporary file {tempPath}: {ioEx.Message}. File will remain.");
            }

            return (!string.IsNullOrEmpty(serializedData), serializedData);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception while serializing shuttle {shuttleUid}: {ex}");
            return (false, null);
        }
    }

    private async Task<EntityUid?> TryDeserializeShuttleAsync(string serializedData)
    {
        try
        {
            // Create a temporary file to load from
            var tempPath = new ResPath($"/drydock_retrieve_{Guid.NewGuid()}.yml");
            var userData = _resourceManager.UserData;

            // Write the serialized data to a temp file asynchronously
            using (var stream = userData.OpenWrite(tempPath))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(serializedData);
            }

            // Create a temporary map to load the grid into
            _map.CreateMap(out var tempMapId);

            // Load the grid using MapLoaderSystem
            if (!_mapLoader.TryLoadGrid(tempMapId, tempPath, out var grid))
            {
                _sawmill.Error($"Failed to load grid from serialized data");
                _map.DeleteMap(tempMapId);

                // Clean up temp file - wrapped in try-catch for Windows file handle issues
                try
                {
                    userData.Delete(tempPath);
                }
                catch (IOException ioEx)
                {
                    _sawmill.Warning($"Failed to delete temporary file {tempPath}: {ioEx.Message}. File will remain.");
                }

                return null;
            }

            var shuttleUid = grid.Value.Owner;

            // Initialize the grid's systems (device networks, atmos, power, etc.)
            // This is important because deserialized entities need their systems reconnected
            var gridInit = new GridInitializeEvent(shuttleUid);
            RaiseLocalEvent(shuttleUid, gridInit, broadcast: true);

            // Force re-initialization of all entities on the grid
            // This ensures components like Destructible, DeviceNetwork, etc. are properly set up
            // We skip PoweredLightComponent entities to avoid duplicating lightbulbs
            var xformQuery = GetEntityQuery<TransformComponent>();
            var allEnts = new HashSet<EntityUid>();
            GetEntitiesOnGrid(shuttleUid, allEnts, xformQuery);

            var reInitCount = 0;
            foreach (var ent in allEnts)
            {
                // Skip powered lights - they already have their bulbs from deserialization
                // and MapInit would spawn duplicate bulbs
                if (HasComp<PoweredLightComponent>(ent))
                    continue;

                // Skip item slots/cabinets - they already have their items from deserialization
                // and MapInit would spawn duplicate items
                if (HasComp<ItemSlotsComponent>(ent))
                    continue;

                // Skip docking ports - they already have their joints from deserialization
                // and MapInit would create duplicate joints
                if (HasComp<DockingComponent>(ent))
                    continue;

                // Trigger MapInit for each entity to reinitialize components
                var mapInitEv = new MapInitEvent();
                RaiseLocalEvent(ent, mapInitEv, broadcast: false);
                reInitCount++;
            }

            _sawmill.Info($"Re-initialized {reInitCount} entities on deserialized grid {shuttleUid} (skipped {allEnts.Count - reInitCount} lights/cabinets/docks)");

            // Clean up temp file - wrapped in try-catch for Windows file handle issues
            try
            {
                userData.Delete(tempPath);
            }
            catch (IOException ioEx)
            {
                _sawmill.Warning($"Failed to delete temporary file {tempPath}: {ioEx.Message}. File will remain.");
            }

            return shuttleUid;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception while deserializing shuttle: {ex}");
            return null;
        }
    }

    private void RefreshState(EntityUid uid, BluespaceDrydockConsoleComponent component)
    {
        var hasIdCard = component.TargetIdSlot.ContainerSlot?.ContainedEntity != null;
        string? activeShipName = null;
        string? storedShipName = null;
        bool hasActiveDeed = false;
        bool hasStoredShip = false;

        if (hasIdCard && component.TargetIdSlot.ContainerSlot?.ContainedEntity is { Valid: true } targetId)
        {
            if (TryComp<ShuttleDeedComponent>(targetId, out var deed))
            {
                hasActiveDeed = deed.ShuttleUid != null;
                activeShipName = ShipyardSystem.GetFullName(deed);
            }

            if (TryComp<BluespaceStorageComponent>(targetId, out var storage))
            {
                hasStoredShip = !string.IsNullOrEmpty(storage.StoredGridData);
                storedShipName = storage.StoredShipFullName;
            }
        }

        var state = new BluespaceDrydockConsoleInterfaceState(
            activeShipName,
            storedShipName,
            hasIdCard,
            hasActiveDeed,
            hasStoredShip
        );

        _ui.SetUiState(uid, BluespaceDrydockConsoleUiKey.Key, state);
    }

    private void PowerDownShipSystems(EntityUid gridUid)
    {
        _sawmill.Info($"Powering down ship systems on grid {gridUid}");

        var xformQuery = GetEntityQuery<TransformComponent>();
        var entities = new HashSet<EntityUid>();
        GetEntitiesOnGrid(gridUid, entities, xformQuery);

        var fuelGenCount = 0;
        var gravGenCount = 0;

        foreach (var entity in entities)
        {
            // Power down fuel generators (PACMANs, etc.)
            if (TryComp<FuelGeneratorComponent>(entity, out var fuelGen) && fuelGen.On)
            {
                _generator.SetFuelGeneratorOn(entity, false, fuelGen);
                fuelGenCount++;
            }

            // Power down gravity generators and set charge to 0
            if (TryComp<PowerChargeComponent>(entity, out var powerCharge) &&
                TryComp<ApcPowerReceiverComponent>(entity, out var powerReceiver))
            {
                if (powerCharge.SwitchedOn)
                {
                    _powerCharge.SetSwitchedOn(entity, powerCharge, false, powerReceiver);
                    gravGenCount++;
                }
            }
        }

        if (fuelGenCount > 0 || gravGenCount > 0)
            _sawmill.Info($"Powered down {fuelGenCount} fuel generators and {gravGenCount} gravity generators");
    }

    private void GetEntitiesOnGrid(EntityUid gridUid, HashSet<EntityUid> entities, EntityQuery<TransformComponent> xformQuery)
    {
        var queue = new Queue<EntityUid>();
        queue.Enqueue(gridUid);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!entities.Add(current))
                continue;

            if (!xformQuery.TryGetComponent(current, out var xform))
                continue;

            var childEnumerator = xform.ChildEnumerator;
            while (childEnumerator.MoveNext(out var child))
            {
                queue.Enqueue(child);
            }
        }
    }

    private void ConsolePopup(EntityUid player, string message)
    {
        _popup.PopupEntity(message, player, player);
    }

    private void PlayDenySound(EntityUid player, EntityUid consoleUid, BluespaceDrydockConsoleComponent component)
    {
        if (_timing.CurTime < component.NextDenySoundTime)
            return;

        component.NextDenySoundTime = _timing.CurTime + component.DenySoundDelay;
        _audio.PlayPvs(component.ErrorSound, consoleUid);
    }

    private void PlayConfirmSound(EntityUid player, EntityUid consoleUid, BluespaceDrydockConsoleComponent component)
    {
        _audio.PlayPvs(component.ConfirmSound, consoleUid);
    }

    #region Public API for ShuttleRecordsSystem

    /// <summary>
    /// Attempts to store a shuttle in bluespace storage. This is the public API for ShuttleRecordsSystem.
    /// </summary>
    /// <param name="player">The player initiating the storage</param>
    /// <param name="shuttleUid">The shuttle to store</param>
    /// <param name="targetId">The ID card to store the ship on</param>
    /// <param name="consoleUid">The console being used (for audio/popups)</param>
    /// <param name="deed">The shuttle deed component</param>
    /// <returns>True if storage was initiated successfully</returns>
    public bool TryStoreShuttleFromRecords(EntityUid player, EntityUid shuttleUid, EntityUid targetId, EntityUid consoleUid, ShuttleDeedComponent deed)
    {
        // Check if there's already a stored ship
        if (HasComp<BluespaceStorageComponent>(targetId))
        {
            ConsolePopup(player, "ID card already has a stored ship!");
            return false;
        }

        // Check if this shuttle is already being processed
        if (_processingStores.Contains(shuttleUid))
        {
            ConsolePopup(player, "Ship is already being stored, please wait...");
            return false;
        }

        // Check if there are any mobs/players on the ship
        var mobQuery = AllEntityQuery<MobStateComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var mobUid, out var _, out var mobXform))
        {
            if (mobXform.GridUid == shuttleUid)
            {
                ConsolePopup(player, "All crew must disembark before storing the ship!");
                return false;
            }
        }

        // Check if the shuttle is docked
        if (!IsShuttleDocked(shuttleUid))
        {
            ConsolePopup(player, "Ship must be docked to store it!");
            return false;
        }

        // Mark as processing
        _processingStores.Add(shuttleUid);

        // Start the multi-step storage process with delays
        ConsolePopup(player, "Beginning ship storage sequence...");

        // Step 1: Power down systems (delayed)
        Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
        {
            if (!Exists(shuttleUid) || !Exists(targetId))
            {
                _processingStores.Remove(shuttleUid);
                return;
            }

            PowerDownShipSystems(shuttleUid);
            ConsolePopup(player, "Ship systems powered down...");

            // Step 2: Delete station (delayed)
            Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
            {
                if (!Exists(shuttleUid) || !Exists(targetId))
                {
                    _processingStores.Remove(shuttleUid);
                    return;
                }

                if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStation)
                {
                    _sawmill.Info($"Deleting station {shuttleStation} before serializing ship {shuttleUid}");

                    _station.DeleteStation(shuttleStation);
                    _sawmill.Info($"Queued station {shuttleStation} for standard deletion lifecycle");
                }

                ConsolePopup(player, "Ship undocking...");

                // Step 3: Undock shuttle (delayed)
                Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                {
                    if (!Exists(shuttleUid) || !Exists(targetId))
                    {
                        _processingStores.Remove(shuttleUid);
                        return;
                    }

                    UndockShuttle(shuttleUid);
                    ConsolePopup(player, "Serializing ship data...");

                    // Step 4: Serialize asynchronously (heavy operation - don't block server)
                    var serializeTask = TrySerializeShuttleAsync(shuttleUid);
                    serializeTask.ContinueWith(task =>
                    {
                        if (!Exists(shuttleUid) || !Exists(targetId))
                        {
                            _processingStores.Remove(shuttleUid);
                            return;
                        }

#pragma warning disable RA0004
                        var (success, serializedData) = task.Result;
#pragma warning restore RA0004

                        if (!success || string.IsNullOrEmpty(serializedData))
                        {
                            ConsolePopup(player, "Failed to serialize ship data!");
                            _processingStores.Remove(shuttleUid);
                            return;
                        }

                        _sawmill.Info($"Successfully serialized ship {shuttleUid}");

                        // Step 5: Finalize storage (delayed)
                        Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                        {
                            if (!Exists(targetId))
                            {
                                _processingStores.Remove(shuttleUid);
                                return;
                            }

                            // Get deed info before we delete the shuttle
                            var shuttleName = deed.ShuttleName;
                            var shuttleFullName = ShipyardSystem.GetFullName(deed);

                            // Store the ship data on the ID card
                            var storage = EnsureComp<BluespaceStorageComponent>(targetId);
                            storage.StoredGridData = serializedData;
                            storage.StoredShipName = shuttleName;
                            storage.StoredShipFullName = shuttleFullName;
                            storage.StoredTime = _timing.CurTime;
                            Dirty(targetId, storage);

                            // Delete the active ship deed and the shuttle
                            RemComp<ShuttleDeedComponent>(targetId);
                            QueueDel(shuttleUid);

                            ConsolePopup(player, $"Ship '{shuttleFullName}' stored successfully!");
                            _processingStores.Remove(shuttleUid);
                            _sawmill.Info($"{ToPrettyString(player)} stored ship {shuttleUid} on ID {ToPrettyString(targetId)}");
                        });
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                });
            });
        });

        return true;
    }

    /// <summary>
    /// Attempts to retrieve a shuttle from bluespace storage. This is the public API for ShuttleRecordsSystem.
    /// </summary>
    /// <param name="player">The player initiating the retrieval</param>
    /// <param name="targetId">The ID card with the stored ship</param>
    /// <param name="consoleUid">The console being used (for audio/popups)</param>
    /// <param name="storage">The bluespace storage component</param>
    /// <param name="dockingTarget">The grid to dock the ship to</param>
    /// <returns>True if retrieval was initiated successfully</returns>
    public bool TryRetrieveShuttleFromRecords(
        EntityUid player,
        EntityUid targetId,
        EntityUid consoleUid,
        BluespaceStorageComponent storage,
        EntityUid dockingTarget,
        Action<EntityUid?>? onCompleted = null)
    {
        // Check if the ID card already has an active ship
        if (HasComp<ShuttleDeedComponent>(targetId))
        {
            ConsolePopup(player, "ID card already has an active ship!");
            return false;
        }

        // Check if there's a stored ship
        if (string.IsNullOrEmpty(storage.StoredGridData))
        {
            ConsolePopup(player, "No stored ship found on ID card!");
            return false;
        }

        // Check if already processing this ID card
        if (_processingRetrieves.Contains(targetId))
        {
            ConsolePopup(player, "Ship is already being retrieved, please wait...");
            return false;
        }

        // Check if docking target is valid
        if (!Exists(dockingTarget))
        {
            ConsolePopup(player, "Invalid docking target!");
            return false;
        }

        var storedData = storage.StoredGridData;
        if (string.IsNullOrEmpty(storedData))
        {
            ConsolePopup(player, "No stored ship found on ID card!");
            return false;
        }

        // Mark as processing
        _processingRetrieves.Add(targetId);

        var storedShipFullName = storage.StoredShipFullName;
        var storedShipName = storage.StoredShipName;

        // Start retrieval process.
        ConsolePopup(player, "Beginning ship retrieval sequence...");
        ConsolePopup(player, "Materializing ship...");

        RunRetrieveFromRecords(
            player,
            targetId,
            dockingTarget,
            storedData,
            storedShipFullName,
            storedShipName,
            onCompleted);

        return true;
    }

    private void RunRetrieveFromRecords(
        EntityUid player,
        EntityUid targetId,
        EntityUid dockingTarget,
        string storedGridData,
        string? storedShipFullName,
        string? storedShipName,
        Action<EntityUid?>? onCompleted)
    {
        var deserializeTask = TryDeserializeShuttleAsync(storedGridData);
        deserializeTask.ContinueWith(task =>
        {
            try
            {
                if (!Exists(targetId))
                {
                    _processingRetrieves.Remove(targetId);
                    onCompleted?.Invoke(null);
                    return;
                }

#pragma warning disable RA0004
                var shuttleUid = task.Result;
#pragma warning restore RA0004

                if (shuttleUid is not { } shuttleUidValue)
                {
                    ConsolePopup(player, "Failed to deserialize ship data!");
                    _processingRetrieves.Remove(targetId);
                    onCompleted?.Invoke(null);
                    return;
                }

                if (!Exists(shuttleUidValue))
                {
                    _processingRetrieves.Remove(targetId);
                    onCompleted?.Invoke(null);
                    return;
                }

                _sawmill.Info($"Successfully deserialized ship {shuttleUidValue}");

                Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                {
                    try
                    {
                        if (!Exists(targetId) || !Exists(shuttleUidValue))
                        {
                            _processingRetrieves.Remove(targetId);
                            onCompleted?.Invoke(null);
                            return;
                        }

                        ConsolePopup(player, "Initializing ship systems...");

                        if (!TryComp<ShuttleComponent>(shuttleUidValue, out var shuttleComp))
                        {
                            ConsolePopup(player, "Retrieved ship is missing shuttle systems!");
                            _processingRetrieves.Remove(targetId);
                            onCompleted?.Invoke(null);
                            return;
                        }

                        shuttleComp.PlayerShuttle = true;

                        var stationConfig = new StationConfig
                        {
                            StationPrototype = "StandardFrontierVessel"
                        };

                        var shuttleStation = _station.InitializeNewStation(stationConfig, new List<EntityUid> { shuttleUidValue });
                        if (!Exists(shuttleStation))
                        {
                            ConsolePopup(player, "Failed to initialize ship station context!");
                            _processingRetrieves.Remove(targetId);
                            onCompleted?.Invoke(null);
                            return;
                        }

                        _station.RenameStation(shuttleStation, storedShipFullName ?? "Unknown Ship", loud: false);

                        Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                        {
                            try
                            {
                                if (!Exists(targetId) || !Exists(shuttleUidValue) || !Exists(dockingTarget))
                                {
                                    _processingRetrieves.Remove(targetId);
                                    onCompleted?.Invoke(null);
                                    return;
                                }

                                ConsolePopup(player, "Docking ship...");

                                var docked = _shuttle.TryFTLDock(shuttleUidValue, shuttleComp, dockingTarget);
                                if (!docked)
                                {
                                    ConsolePopup(player, "Failed to dock retrieved ship!");
                                    _processingRetrieves.Remove(targetId);
                                    onCompleted?.Invoke(null);
                                    return;
                                }

                                Timer.Spawn(TimeSpan.FromSeconds(ProcessingDelaySeconds), () =>
                                {
                                    try
                                    {
                                        if (!Exists(targetId) || !Exists(shuttleUidValue))
                                        {
                                            _processingRetrieves.Remove(targetId);
                                            onCompleted?.Invoke(null);
                                            return;
                                        }

                                        var shuttleDeed = EnsureComp<ShuttleDeedComponent>(targetId);
                                        shuttleDeed.ShuttleUid = shuttleUidValue;
                                        shuttleDeed.ShuttleName = storedShipName ?? "Unknown";
                                        shuttleDeed.ShuttleNameSuffix = string.Empty;
                                        shuttleDeed.ShuttleOwner = "Unknown";
                                        Dirty(targetId, shuttleDeed);

                                        if (HasComp<BluespaceStorageComponent>(targetId))
                                            RemComp<BluespaceStorageComponent>(targetId);

                                        ConsolePopup(player, $"Ship '{storedShipFullName}' retrieved successfully!");
                                        _sawmill.Info($"{ToPrettyString(player)} retrieved ship {shuttleUidValue} from ID {ToPrettyString(targetId)}");
                                        onCompleted?.Invoke(shuttleUidValue);
                                    }
                                    catch (Exception ex)
                                    {
                                        HandleRetrieveFailure(player, targetId, onCompleted, ex);
                                    }
                                    finally
                                    {
                                        _processingRetrieves.Remove(targetId);
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                HandleRetrieveFailure(player, targetId, onCompleted, ex);
                                _processingRetrieves.Remove(targetId);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        HandleRetrieveFailure(player, targetId, onCompleted, ex);
                        _processingRetrieves.Remove(targetId);
                    }
                });
            }
            catch (Exception ex)
            {
                HandleRetrieveFailure(player, targetId, onCompleted, ex);
                _processingRetrieves.Remove(targetId);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void HandleRetrieveFailure(
        EntityUid player,
        EntityUid targetId,
        Action<EntityUid?>? onCompleted,
        Exception ex)
    {
        _sawmill.Error($"Exception while retrieving ship from records: {ex}");

        if (Exists(targetId))
            ConsolePopup(player, "Ship retrieval failed due to an internal error.");

        onCompleted?.Invoke(null);
    }

    #endregion
}
