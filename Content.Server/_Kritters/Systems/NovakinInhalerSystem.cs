using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Prototypes;
using Content.Shared._Kritters;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.Systems;

/// <summary>
/// Converts gas held by the native tank component into Novakin reserve. Gas
/// storage, canister refilling, and pressure behavior remain owned by atmos.
/// </summary>
public sealed partial class NovakinInhalerSystem : EntitySystem
{
    [Dependency] private NovakinPhysiologySystem _physiology = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NovakinInhalerComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<NovakinInhalerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<NovakinInhalerComponent, ExaminedEvent>(OnExamined);
    }

    private void OnUseInHand(Entity<NovakinInhalerComponent> inhaler, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryTransfer(inhaler, args.User, args.User);
    }

    private void OnAfterInteract(Entity<NovakinInhalerComponent> inhaler, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryComp<GasTankComponent>(target, out var sourceTank))
        {
            args.Handled = TryRefillFromTank(inhaler, (target, sourceTank), args.User);
            return;
        }

        args.Handled = TryTransfer(inhaler, target, args.User);
    }

    private bool TryTransfer(Entity<NovakinInhalerComponent> inhaler, EntityUid target, EntityUid user)
    {
        if (!TryComp<NovakinPhysiologyComponent>(target, out var physiology))
        {
            _popup.PopupEntity(Loc.GetString("novakin-inhaler-not-novakin"), target, user);
            return true;
        }

        if (!TryComp<GasTankComponent>(inhaler, out var tank)
            || !_prototypes.TryIndex(physiology.Gas, out var gasPrototype)
            || !_prototypes.TryIndex(inhaler.Comp.Gas, out var inhalerGas))
        {
            return false;
        }

        if (physiology.CurrentReserve >= physiology.MaxReserve)
        {
            _popup.PopupEntity(Loc.GetString("novakin-inhaler-reserve-full"), target, user);
            return true;
        }

        if (inhalerGas.Gas != gasPrototype.Gas || !ContainsOnly(tank.Air, gasPrototype.Gas))
        {
            _popup.PopupEntity(Loc.GetString("novakin-inhaler-gas-mismatch",
                ("gas", Loc.GetString(gasPrototype.Name))), target, user);
            return true;
        }

        var availableMoles = tank.Air.GetMoles(gasPrototype.Gas);
        if (availableMoles <= Atmospherics.GasMinMoles)
        {
            _popup.PopupEntity(Loc.GetString("novakin-inhaler-empty"), inhaler, user);
            return true;
        }

        if (!_useDelay.TryResetDelay(inhaler, checkDelayed: true))
            return true;

        var missingReserve = physiology.MaxReserve - physiology.CurrentReserve;
        var reserve = Math.Min(inhaler.Comp.TransferAmount, missingReserve);
        reserve = Math.Min(reserve, availableMoles * inhaler.Comp.ReservePerMole);
        var accepted = _physiology.AddReserve((target, physiology), physiology.Gas, reserve);
        if (accepted <= 0f)
            return true;

        tank.Air.AdjustMoles(gasPrototype.Gas, -accepted / inhaler.Comp.ReservePerMole);
        Dirty(inhaler.Owner, tank);

        _popup.PopupEntity(Loc.GetString("novakin-inhaler-success",
            ("amount", NovakinDisplayFormat.Number(accepted)),
            ("gas", Loc.GetString(gasPrototype.Name))), target, user);
        return true;
    }

    private bool TryRefillFromTank(
        Entity<NovakinInhalerComponent> inhaler,
        Entity<GasTankComponent> sourceTank,
        EntityUid user)
    {
        if (!TryComp<GasTankComponent>(inhaler, out var tank)
            || !_prototypes.TryIndex(inhaler.Comp.Gas, out var gasPrototype))
        {
            return false;
        }

        if (!ContainsOnly(sourceTank.Comp.Air, gasPrototype.Gas))
        {
            _popup.PopupEntity(Loc.GetString("novakin-inhaler-refill-mismatch",
                ("gas", Loc.GetString(gasPrototype.Name))), inhaler, user);
            return true;
        }

        var stored = tank.Air.GetMoles(gasPrototype.Gas);
        var capacity = Math.Max(0f, inhaler.Comp.MaxMoles - stored);
        if (capacity <= Atmospherics.GasMinMoles)
        {
            _popup.PopupEntity(Loc.GetString("novakin-inhaler-refill-full"), inhaler, user);
            return true;
        }

        if (!_useDelay.TryResetDelay(inhaler, checkDelayed: true))
            return true;

        var transferred = Math.Min(capacity, sourceTank.Comp.Air.GetMoles(gasPrototype.Gas));
        sourceTank.Comp.Air.AdjustMoles(gasPrototype.Gas, -transferred);
        tank.Air.AdjustMoles(gasPrototype.Gas, transferred);
        Dirty(sourceTank);
        Dirty(inhaler.Owner, tank);

        _popup.PopupEntity(Loc.GetString("novakin-inhaler-refill-success",
            ("gas", Loc.GetString(gasPrototype.Name))), inhaler, user);
        return true;
    }

    private void OnExamined(Entity<NovakinInhalerComponent> inhaler, ref ExaminedEvent args)
    {
        if (!TryComp<GasTankComponent>(inhaler, out var tank))
            return;

        if (!_prototypes.TryIndex(inhaler.Comp.Gas, out var gasPrototype))
            return;

        if (tank.Air.GetMoles(gasPrototype.Gas) <= Atmospherics.GasMinMoles)
        {
            args.PushMarkup(Loc.GetString("novakin-inhaler-examine-empty",
                ("gas", Loc.GetString(gasPrototype.Name))));
            return;
        }

        var reserve = tank.Air.GetMoles(gasPrototype.Gas) * inhaler.Comp.ReservePerMole;
        var uses = (int) MathF.Ceiling(reserve / inhaler.Comp.TransferAmount);
        args.PushMarkup(Loc.GetString("novakin-inhaler-examine",
            ("gas", Loc.GetString(gasPrototype.Name)),
            ("uses", uses)));
    }

    private static bool ContainsOnly(GasMixture mixture, Gas expected)
    {
        if (mixture.GetMoles(expected) <= Atmospherics.GasMinMoles)
            return false;

        foreach (var (gas, moles) in mixture)
        {
            if (gas != expected && moles > Atmospherics.GasMinMoles)
                return false;
        }

        return true;
    }
}
