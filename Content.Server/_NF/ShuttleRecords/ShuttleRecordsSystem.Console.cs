using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Cargo.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared._NF.ShuttleRecords.Components;
using Content.Shared._NF.ShuttleRecords.Events;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared._NF.Shipyard.Components;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._NF.ShuttleRecords;

public sealed partial class ShuttleRecordsSystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private BluespaceDrydockSystem _drydock = default!;

    public void InitializeShuttleRecords()
    {
        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, CopyDeedMessage>(OnCopyDeedMessage);
        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, StoreShuttleMessage>(OnStoreShuttleMessage);
        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, RetrieveShuttleMessage>(OnRetrieveShuttleMessage);

        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, EntInsertedIntoContainerMessage>(OnIDSlotUpdated);
        SubscribeLocalEvent<ShuttleRecordsConsoleComponent, EntRemovedFromContainerMessage>(OnIDSlotUpdated);
    }

    private void OnConsoleUiOpened(EntityUid uid, ShuttleRecordsConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true })
            return;

        RefreshState(uid, component);
    }

    private void RefreshState(EntityUid consoleUid, ShuttleRecordsConsoleComponent? component, bool skipRecords = false)
    {
        if (!Resolve(consoleUid, ref component))
            return;

        // Ensures that when this console is no longer attached to a grid and is powered somehow, it won't work.
        if (Transform(consoleUid).GridUid == null)
            return;

        if (!TryGetShuttleRecordsDataComponent(out var dataComponent))
            return;

        var targetIdEntity = component.TargetIdSlot.ContainerSlot?.ContainedEntity;
        bool targetIdValid = targetIdEntity is { Valid: true };
        string? targetIdFullName = null;
        string? targetIdVesselName = null;
        if (targetIdValid)
        {
            try
            {
                targetIdFullName = Name(targetIdEntity!.Value);
            }
            catch (KeyNotFoundException)
            {
                targetIdFullName = "";
            }
        }

        if (EntityManager.TryGetComponent(targetIdEntity, out ShuttleDeedComponent? shuttleDeed))
            targetIdVesselName = shuttleDeed.ShuttleName + " " + shuttleDeed.ShuttleNameSuffix;

        var newState = new ShuttleRecordsConsoleInterfaceState(
            records: skipRecords ? null : dataComponent.ShuttleRecords.Values.ToList(),
            isTargetIdPresent: targetIdValid,
            targetIdFullName: targetIdFullName,
            targetIdVesselName: targetIdVesselName,
            transactionPercentage: component.TransactionPercentage,
            minTransactionPrice: component.MinTransactionPrice,
            maxTransactionPrice: component.MaxTransactionPrice,
            fixedTransactionPrice: component.FixedTransactionPrice
        );

        _ui.SetUiState(consoleUid, ShuttleRecordsUiKey.Default, newState);
    }

    // TODO: private interface, listen to messages that would add ship records
    public void RefreshStateForAll(bool skipRecords = false)
    {
        if (!TryGetShuttleRecordsDataComponent(out var dataComponent))
            return;
        List<ShuttleRecord>? records = null;
        if (!skipRecords)
            records = dataComponent.ShuttleRecords.Values.ToList();
        var query = EntityQueryEnumerator<ShuttleRecordsConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var component))
        {
            // Ensures that when this console is no longer attached to a grid and is powered somehow, it won't work.
            if (Transform(consoleUid).GridUid == null)
                continue;

            var targetIdEntity = component.TargetIdSlot.ContainerSlot?.ContainedEntity;
            bool targetIdValid = targetIdEntity is { Valid: true };
            string? targetIdFullName = null;
            string? targetIdVesselName = null;
            if (targetIdValid)
            {
                try
                {
                    targetIdFullName = Name(targetIdEntity!.Value);
                }
                catch (KeyNotFoundException)
                {
                    targetIdFullName = "";
                }
            }

            if (EntityManager.TryGetComponent(targetIdEntity, out ShuttleDeedComponent? shuttleDeed))
                targetIdVesselName = shuttleDeed.ShuttleName + " " + shuttleDeed.ShuttleNameSuffix;

            var newState = new ShuttleRecordsConsoleInterfaceState(
                records: records,
                isTargetIdPresent: targetIdValid,
                targetIdFullName: targetIdFullName,
                targetIdVesselName: targetIdVesselName,
                transactionPercentage: component.TransactionPercentage,
                minTransactionPrice: component.MinTransactionPrice,
                maxTransactionPrice: component.MaxTransactionPrice,
                fixedTransactionPrice: component.FixedTransactionPrice
            );

            _ui.SetUiState(consoleUid, ShuttleRecordsUiKey.Default, newState);
        }
    }

    private void OnCopyDeedMessage(EntityUid uid, ShuttleRecordsConsoleComponent component, CopyDeedMessage args)
    {
        if (!TryGetShuttleRecordsDataComponent(out var dataComponent))
            return;

        // Check if id card is present.
        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            _popup.PopupEntity(Loc.GetString("shuttle-records-no-idcard"), args.Actor);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Check if the actor has access to the shuttle records console.
        if (!_access.IsAllowed(args.Actor, uid))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-records-no-access"), args.Actor);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Check if the shuttle record exists.
        var record = dataComponent.ShuttleRecords.Values.Select(record => record).FirstOrDefault(record => record.EntityUid == args.ShuttleNetEntity);
        if (record == null)
        {
            _popup.PopupEntity(Loc.GetString("shuttle-records-no-record-found"), args.Actor);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Ensure that after the deduction math there is more than 0 left in the account.
        var transactionPrice = GetTransactionCost(component, record.PurchasePrice);
        if (!_bank.TrySectorWithdraw(component.Account, (int)transactionPrice, LedgerEntryType.ShuttleRecordFees))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-records-insufficient-funds"), args.Actor);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        AssignShuttleDeedProperties(record, targetId);

        // Refreshing the state, so that the newly applied deed is shown in the UI.
        // We cannot do this client side because of the checks that we have to do serverside.
        RefreshState(uid, component);

        // Add to admin logs.
        var shuttleName = record.Name + " " + record.Suffix;
        _adminLogger.Add(
            LogType.ShuttleRecordsUsage,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):actor} used {transactionPrice} from station bank account to copy shuttle deed {shuttleName}.");
        _audioSystem.PlayPredicted(component.ConfirmSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
    }

    private void OnIDSlotUpdated(EntityUid uid, ShuttleRecordsConsoleComponent component, EntityEventArgs args)
    {
        if (!component.Initialized)
            return;

        // Slot updated, no need to resend entire record set
        RefreshState(uid, component, true);
    }

    private void AssignShuttleDeedProperties(ShuttleRecord shuttleRecord, EntityUid targetId)
    {
        // Ensure that this is in fact a id card.
        if (!_entityManager.TryGetComponent<IdCardComponent>(targetId, out _))
            return;

        _entityManager.EnsureComponent<ShuttleDeedComponent>(targetId, out var deed);

        var shuttleEntity = _entityManager.GetEntity(shuttleRecord.EntityUid);

        // Copy over the variables from the shuttle record to the deed.
        deed.ShuttleUid = shuttleEntity;
        deed.ShuttleOwner = shuttleRecord.OwnerName;
        deed.ShuttleName = shuttleRecord.Name;
        deed.ShuttleNameSuffix = shuttleRecord.Suffix;
        deed.PurchasedWithVoucher = shuttleRecord.PurchasedWithVoucher;
        Dirty(targetId, deed);
    }

    /// <summary>
    /// Get the transaction cost for the given shipyard and sell value.
    /// </summary>
    /// <param name="component">The shuttle records console component</param>
    /// <param name="vesselPrice">The cost to purchase the ship</param>
    /// <returns>The transaction cost for this ship.</returns>
    public static uint GetTransactionCost(ShuttleRecordsConsoleComponent component, uint vesselPrice)
    {
        return GetTransactionCost(
            percent: component.TransactionPercentage,
            min: component.MinTransactionPrice,
            max: component.MaxTransactionPrice,
            fixedPrice: component.FixedTransactionPrice,
            vesselPrice: vesselPrice
        );
    }

    private void OnStoreShuttleMessage(EntityUid uid, ShuttleRecordsConsoleComponent component, StoreShuttleMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Get the station grid this console is on
        var consoleGrid = Transform(uid).GridUid;
        if (consoleGrid == null)
        {
            _popup.PopupEntity("Console must be on a station grid!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Check access
        if (!_access.IsAllowed(player, uid))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-records-no-access"), player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Get the shuttle entity
        var shuttleEntity = GetEntity(args.ShuttleNetEntity);
        if (!Exists(shuttleEntity))
        {
            _popup.PopupEntity("Shuttle no longer exists!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Get the shuttle record
        if (!TryGetRecord(args.ShuttleNetEntity, out var record))
        {
            _popup.PopupEntity("No shuttle record found!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Check if already stored
        if (!string.IsNullOrEmpty(record.StoredGridData))
        {
            _popup.PopupEntity("Shuttle is already stored!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Try to store the shuttle using the drydock system
        // We'll use a temporary storage entity to hold the data during the process
        var tempStorage = Spawn(null, MapCoordinates.Nullspace);

        // Create a temporary deed to pass to the drydock system
        var tempDeed = EnsureComp<ShuttleDeedComponent>(tempStorage);
        tempDeed.ShuttleUid = shuttleEntity;
        tempDeed.ShuttleName = record.Name;
        tempDeed.ShuttleNameSuffix = record.Suffix ?? "";
        tempDeed.ShuttleOwner = record.OwnerName;

        if (_drydock.TryStoreShuttleFromRecords(player, shuttleEntity, tempStorage, uid, tempDeed))
        {
            // Schedule a delayed check to retrieve the stored data and update the record
            // Wait 3 seconds to ensure all drydock operations complete (power down, serialize, etc.)
            Timer.Spawn(TimeSpan.FromSeconds(3.0), () =>
            {
                if (!Exists(tempStorage))
                    return;

                if (TryComp<BluespaceStorageComponent>(tempStorage, out var storage) && !string.IsNullOrEmpty(storage.StoredGridData))
                {
                    // Update the shuttle record with the stored data
                    record.StoredGridData = storage.StoredGridData;
                    record.StoredShipFullName = storage.StoredShipFullName;
                    TryUpdateRecord(record);
                    RefreshStateForAll();
                }

                QueueDel(tempStorage);
            });

            _audioSystem.PlayPredicted(component.ConfirmSound, uid, null, AudioParams.Default.WithMaxDistance(5f));

            // Add to admin logs
            _adminLogger.Add(
                LogType.ShuttleRecordsUsage,
                LogImpact.Medium,
                $"{ToPrettyString(player):actor} stored shuttle {ToPrettyString(shuttleEntity)} using shuttle records console.");
        }
        else
        {
            QueueDel(tempStorage);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
        }
    }

    private void OnRetrieveShuttleMessage(EntityUid uid, ShuttleRecordsConsoleComponent component, RetrieveShuttleMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        // Get the station grid this console is on
        var consoleGrid = Transform(uid).GridUid;
        if (consoleGrid == null)
        {
            _popup.PopupEntity("Console must be on a station grid!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Check access
        if (!_access.IsAllowed(player, uid))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-records-no-access"), player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Get the shuttle record
        if (!TryGetRecord(args.ShuttleNetEntity, out var record))
        {
            _popup.PopupEntity("No shuttle record found!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Check if there's stored data
        if (string.IsNullOrEmpty(record.StoredGridData))
        {
            _popup.PopupEntity("No stored ship data found!", player);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Create a temporary entity to hold the storage component
        var tempStorage = Spawn(null, MapCoordinates.Nullspace);
        var storageComp = EnsureComp<BluespaceStorageComponent>(tempStorage);
        storageComp.StoredGridData = record.StoredGridData;
        storageComp.StoredShipFullName = record.StoredShipFullName;
        storageComp.StoredShipName = record.Name;

        // Try to retrieve the shuttle using the drydock system (dock to the console's grid)
        if (_drydock.TryRetrieveShuttleFromRecords(
                player,
                tempStorage,
                uid,
                storageComp,
                consoleGrid.Value,
                retrievedShuttleUid =>
                {
                    if (!Exists(tempStorage))
                        return;

                    if (retrievedShuttleUid is not { } shuttleUid)
                    {
                        QueueDel(tempStorage);
                        return;
                    }

                    // Store the old entity UID before we change it.
                    var oldEntityUid = record.EntityUid;
                    record.EntityUid = GetNetEntity(shuttleUid);

                    // Clear stored data only after explicit retrieval success.
                    record.StoredGridData = null;
                    record.StoredShipFullName = null;

                    if (!TryGetShuttleRecordsDataComponent(out var dataComponent))
                    {
                        QueueDel(tempStorage);
                        return;
                    }

                    // Move record key to the new active shuttle entity.
                    dataComponent.ShuttleRecords.Remove(oldEntityUid);
                    dataComponent.ShuttleRecords[record.EntityUid] = record;

                    RefreshStateForAll();
                    QueueDel(tempStorage);
                }))
        {
            _audioSystem.PlayPredicted(component.ConfirmSound, uid, null, AudioParams.Default.WithMaxDistance(5f));

            // Add to admin logs
            _adminLogger.Add(
                LogType.ShuttleRecordsUsage,
                LogImpact.Medium,
                $"{ToPrettyString(player):actor} retrieved shuttle from storage using shuttle records console.");
        }
        else
        {
            QueueDel(tempStorage);
            _audioSystem.PlayPredicted(component.ErrorSound, uid, null, AudioParams.Default.WithMaxDistance(5f));
        }
    }
}
