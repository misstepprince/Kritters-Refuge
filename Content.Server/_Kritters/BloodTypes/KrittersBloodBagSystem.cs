using Content.Server._Kritters.BloodTypes.Components;
using Content.Server.VendingMachines;
using Content.Shared._Kritters.CCVar;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.VendingMachines;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.BloodTypes;

public sealed class KrittersBloodBagSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly KrittersBloodCompatibilitySystem _compatibility = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private readonly HashSet<EntityUid> _attachedBags = new();
    private readonly List<EntityUid> _bagsToDetach = new();
    private EntityQuery<KrittersBloodBagComponent> _bloodBagQuery;

    public override void Initialize()
    {
        _bloodBagQuery = GetEntityQuery<KrittersBloodBagComponent>();

        SubscribeLocalEvent<KrittersBloodTypesOnlyComponent, ComponentStartup>(OnBloodTypesOnlyStartup);
        SubscribeLocalEvent<KrittersBloodTypesOnlyComponent, AfterInteractEvent>(OnBloodTypesOnlyAfterInteract);
        SubscribeLocalEvent<KrittersBloodTypesOnlyComponent, UseInHandEvent>(OnBloodTypesOnlyUseInHand);
        SubscribeLocalEvent<KrittersBloodTypeVendingStockComponent, MapInitEvent>(OnVendingMapInit, after: [typeof(VendingMachineSystem)]);
        SubscribeLocalEvent<KrittersBloodOnlyContainerComponent, SolutionTransferAttemptEvent>(OnSolutionTransferAttempt);
        SubscribeLocalEvent<KrittersBloodOnlyContainerComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<KrittersBloodBagComponent, AfterInteractEvent>(OnBloodBagAfterInteract);
        SubscribeLocalEvent<KrittersBloodBagComponent, UseInHandEvent>(OnBloodBagUseInHand);
        SubscribeLocalEvent<KrittersBloodBagComponent, DroppedEvent>(OnBloodBagDropped);
        SubscribeLocalEvent<KrittersBloodBagComponent, GotUnequippedHandEvent>(OnBloodBagUnequipped);
        SubscribeLocalEvent<KrittersBloodBagComponent, ComponentShutdown>(OnBloodBagShutdown);
    }

    public override void Update(float frameTime)
    {
        foreach (var uid in _attachedBags)
        {
            if (!_bloodBagQuery.TryComp(uid, out var bag)
                || bag.AttachedTarget == null
                || !BloodTypesEnabled
                || !TryDrip((uid, bag), frameTime))
            {
                _bagsToDetach.Add(uid);
            }
        }

        foreach (var uid in _bagsToDetach)
        {
            if (_bloodBagQuery.TryComp(uid, out var bag))
            {
                Detach((uid, bag));
                continue;
            }

            _attachedBags.Remove(uid);
        }

        _bagsToDetach.Clear();
    }

    private bool BloodTypesEnabled => _cfg.GetCVar(KrittersCCVars.BloodTypesEnabled);

    private void OnBloodTypesOnlyStartup(Entity<KrittersBloodTypesOnlyComponent> ent, ref ComponentStartup args)
    {
        if (!BloodTypesEnabled)
            QueueDel(ent);
    }

    private void OnBloodTypesOnlyAfterInteract(Entity<KrittersBloodTypesOnlyComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || BloodTypesEnabled)
            return;

        _popup.PopupEntity(Loc.GetString("kritters-blood-types-item-disabled"), ent, args.User);
        args.Handled = true;
    }

    private void OnBloodTypesOnlyUseInHand(Entity<KrittersBloodTypesOnlyComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || BloodTypesEnabled)
            return;

        _popup.PopupEntity(Loc.GetString("kritters-blood-types-item-disabled"), ent, args.User);
        args.Handled = true;
    }

    private void OnVendingMapInit(Entity<KrittersBloodTypeVendingStockComponent> ent, ref MapInitEvent args)
    {
        if (!BloodTypesEnabled || !TryComp<VendingMachineComponent>(ent, out var vending))
        {
            return;
        }

        foreach (var (id, amount) in ent.Comp.Entries)
        {
            if (vending.Inventory.ContainsKey(id.Id) || !_proto.HasIndex<EntityPrototype>(id))
                continue;

            vending.Inventory[id.Id] = new VendingMachineInventoryEntry(
                InventoryType.Regular,
                id.Id,
                amount);
        }

        Dirty(ent.Owner, vending);
    }

    private void OnSolutionTransferAttempt(Entity<KrittersBloodOnlyContainerComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.To != ent.Owner)
            return;

        if (!BloodTypesEnabled)
        {
            args.Cancel(Loc.GetString("kritters-blood-bag-disabled"));
            return;
        }

        if (ent.Comp.ReagentWhitelist.Count == 0
            || !_solutions.TryGetDrainableSolution(args.From, out _, out var sourceSolution)
            || IsBloodOnly(sourceSolution, ent.Comp.ReagentWhitelist))
        {
            return;
        }

        args.Cancel(Loc.GetString("kritters-blood-bag-non-blood"));
    }

    private void OnSolutionChanged(Entity<KrittersBloodOnlyContainerComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != ent.Comp.Solution)
        {
            return;
        }

        RemoveNonBloodReagents(ent);
    }

    private void OnBloodBagAfterInteract(Entity<KrittersBloodBagComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { Valid: true } target)
            return;

        if (!BloodTypesEnabled)
        {
            _popup.PopupEntity(Loc.GetString("kritters-blood-bag-disabled"), ent, args.User);
            args.Handled = true;
            return;
        }

        if (!HasComp<BloodstreamComponent>(target))
            return;

        args.Handled = true;

        if (ent.Comp.AttachedTarget == target)
        {
            Detach(ent, args.User, "kritters-blood-bag-disconnected");
            return;
        }

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
            || solution.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("kritters-blood-bag-empty"), ent, args.User);
            return;
        }

        ent.Comp.AttachedTarget = target;
        ent.Comp.AttachedUser = args.User;
        ent.Comp.Accumulator = 0f;
        _attachedBags.Add(ent.Owner);

        _popup.PopupEntity(Loc.GetString("kritters-blood-bag-connected", ("target", target)), ent, args.User);
    }

    private void OnBloodBagUseInHand(Entity<KrittersBloodBagComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.AttachedTarget == null)
            return;

        Detach(ent, args.User, "kritters-blood-bag-disconnected");
        args.Handled = true;
    }

    private void OnBloodBagDropped(Entity<KrittersBloodBagComponent> ent, ref DroppedEvent args)
    {
        Detach(ent);
    }

    private void OnBloodBagUnequipped(Entity<KrittersBloodBagComponent> ent, ref GotUnequippedHandEvent args)
    {
        Detach(ent);
    }

    private void OnBloodBagShutdown(Entity<KrittersBloodBagComponent> ent, ref ComponentShutdown args)
    {
        _attachedBags.Remove(ent.Owner);
    }

    private bool TryDrip(Entity<KrittersBloodBagComponent> bag, float frameTime)
    {
        if (bag.Comp.AttachedTarget is not { Valid: true } target
            || bag.Comp.AttachedUser is not { Valid: true } user
            || Deleted(target)
            || Deleted(user)
            || !InConnectionRange(bag, user, target)
            || !TryComp<BloodstreamComponent>(target, out var bloodstream)
            || !_solutions.TryGetSolution(bag.Owner, bag.Comp.Solution, out var bagSolutionEnt, out var bagSolution)
            || !_solutions.ResolveSolution(target, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution)
            || bagSolution.Volume <= 0
            || bloodSolution.AvailableVolume <= 0)
        {
            return false;
        }

        bag.Comp.Accumulator += frameTime;
        var amount = bag.Comp.TransferRate * bag.Comp.Accumulator;
        if (amount <= 0)
            return true;

        var transferAmount = FixedPoint2.Min(amount, bagSolution.Volume, bloodSolution.AvailableVolume);
        if (transferAmount <= 0)
            return false;

        bag.Comp.Accumulator = 0f;

        var transfusion = _solutions.SplitSolution(bagSolutionEnt.Value, transferAmount);
        if (transfusion.Volume <= 0)
            return false;

        _compatibility.ApplyTransfusionMistreatment(target, transfusion, bag.Owner);
        _solutions.TryAddSolution(bloodstream.BloodSolution.Value, transfusion);

        return true;
    }

    private bool InConnectionRange(Entity<KrittersBloodBagComponent> bag, EntityUid user, EntityUid target)
    {
        return Transform(user).Coordinates.TryDistance(EntityManager, Transform(target).Coordinates, out var distance)
            && distance <= bag.Comp.MaxConnectionRange;
    }

    private void Detach(Entity<KrittersBloodBagComponent> ent, EntityUid? user = null, string? popup = null)
    {
        if (ent.Comp.AttachedTarget == null && ent.Comp.AttachedUser == null)
            return;

        ent.Comp.AttachedTarget = null;
        ent.Comp.AttachedUser = null;
        ent.Comp.Accumulator = 0f;
        _attachedBags.Remove(ent.Owner);

        if (user != null && popup != null)
            _popup.PopupEntity(Loc.GetString(popup), ent, user.Value);
    }

    private void RemoveNonBloodReagents(Entity<KrittersBloodOnlyContainerComponent> ent)
    {
        if (ent.Comp.ReagentWhitelist.Count == 0
            || !_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var solutionEnt, out var solution))
        {
            return;
        }

        for (var i = solution.Contents.Count - 1; i >= 0; i--)
        {
            var reagent = solution.Contents[i].Reagent.Prototype;
            if (IsAllowedReagent(reagent, ent.Comp.ReagentWhitelist))
                continue;

            _solutions.RemoveReagent(solutionEnt.Value, solution.Contents[i]);
        }
    }

    private bool IsBloodOnly(Solution solution, List<ProtoId<ReagentPrototype>> whitelist)
    {
        if (whitelist.Count == 0)
            return true;

        foreach (var reagent in solution.Contents)
        {
            if (!IsAllowedReagent(reagent.Reagent.Prototype, whitelist))
                return false;
        }

        return true;
    }

    private bool IsAllowedReagent(ProtoId<ReagentPrototype> reagent, List<ProtoId<ReagentPrototype>> whitelist)
    {
        foreach (var allowed in whitelist)
        {
            if (allowed == reagent)
                return true;
        }

        return false;
    }
}
