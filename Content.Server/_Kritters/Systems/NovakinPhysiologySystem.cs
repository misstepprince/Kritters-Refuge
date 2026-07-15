using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Server.Stack;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._CS.Needs;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.EntityEffects;
using Content.Shared._Kritters.Systems;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;

namespace Content.Server._Kritters.Systems;

/// <summary>Compact nitrogen reserve, core fuel, and thermal-shell physiology.</summary>
public sealed partial class NovakinPhysiologySystem : SharedNovakinPhysiologySystem
{
    private const float DangerousCold = 323.15f;
    private const float DangerousHeat = 700f;
    private const float CriticalDamage = 100f;
    private const float ThermalDamagePerSecond = CriticalDamage / 30f;

    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedNeedsSystem _needs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private StomachSystem _stomach = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NovakinFuelMaterialComponent, UseInHandEvent>(OnFuelMaterialUsed);
        SubscribeLocalEvent<NovakinPhysiologyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<ExecuteEntityEffectEvent<NovakinHeat>>(OnNovakinHeat);
        SubscribeLocalEvent<NovakinPhysiologyComponent, NovakinCryoPodInjectionEvent>(OnCryoPodInjection);
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < 0.5f)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;
        var query = EntityQueryEnumerator<NovakinPhysiologyComponent, NeedsComponent, TemperatureComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var physiology, out var needs, out var temperature, out var transform))
        {
            if (!TryComp<MobStateComponent>(uid, out var mobState))
                continue;

            UpdateFuelConsumptionRate(physiology, needs, temperature);
            UpdateThermalRegulation(uid, physiology, needs);
            UpdateHeatSpeed(uid, physiology, temperature);
            CoolWhenFuelDepleted(uid, physiology, needs, temperature, elapsed);
            ApplyThermalShellDamage(uid, physiology, temperature, mobState, elapsed);

            var lost = 0f;
            if (!_mobState.IsDead(uid, mobState))
            {
                var drain = GetReserveDrainRate(uid, physiology);
                lost = RemoveReserve((uid, physiology), drain * elapsed);
            }

            if (lost > 0f)
            {
                var mixture = _atmosphere.GetTileMixture((uid, transform), excite: true);
                mixture?.AdjustMoles(Gas.Nitrogen, lost * physiology.LeakedMolesPerReserve);
            }

            ApplyGasBloodloss(uid, physiology, mobState);
            ClearRecoveredShell(uid, physiology, temperature, mobState);
        }
    }

    private void ApplyThermalShellDamage(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature, MobStateComponent mobState, float elapsed)
    {
        if (_mobState.IsCritical(uid, mobState) || _mobState.IsDead(uid, mobState)
            || (temperature.CurrentTemperature >= DangerousCold && temperature.CurrentTemperature <= DangerousHeat)
            || !TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var remaining = CriticalDamage - damageable.TotalDamage.Float();
        if (remaining > 0f)
        {
            var thermalDamage = ThermalDamagePerSecond * elapsed;
            if (temperature.CurrentTemperature < DangerousCold)
                thermalDamage *= 0.5f;

            _damageable.TryChangeDamage(uid, new DamageSpecifier { DamageDict = { ["Blunt"] = Math.Min(thermalDamage, remaining) } },
                ignoreResistances: true, interruptsDoAfters: false);
        }

        // Damage state updates synchronously; thermal exposure is never allowed to deal beyond critical.
        if (damageable.TotalDamage.Float() >= CriticalDamage)
        {
            physiology.ShellShattered = true;
            Dirty(uid, physiology);
        }
    }

    private void ClearRecoveredShell(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature, MobStateComponent mobState)
    {
        if (physiology.ShellShattered && !_mobState.IsCritical(uid, mobState)
            && !_mobState.IsDead(uid, mobState)
            && temperature.CurrentTemperature >= DangerousCold && temperature.CurrentTemperature <= DangerousHeat)
        {
            physiology.ShellShattered = false;
            Dirty(uid, physiology);
        }
    }

    private void ApplyGasBloodloss(EntityUid uid, NovakinPhysiologyComponent physiology, MobStateComponent mobState)
    {
        if (_mobState.IsDead(uid, mobState))
            return;

        var fraction = physiology.MaxReserve <= 0f ? 0f : Math.Clamp(physiology.CurrentReserve / physiology.MaxReserve, 0f, 1f);
        // Mirror the shared bloodstream's damage/recovery curves, substituting nitrogen percentage.
        if (fraction < physiology.BloodlossReserveThreshold)
        {
            _damageable.TryChangeDamage(uid, physiology.BloodlossDamage / (0.1f + fraction),
                interruptsDoAfters: false);
        }
        else
        {
            _damageable.TryChangeDamage(uid, physiology.BloodlossHealDamage * fraction,
                ignoreResistances: true, interruptsDoAfters: false);
        }
    }

    private float GetReserveDrainRate(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        if (physiology.ShellShattered)
            return physiology.ShatteredReserveDrainPerSecond;

        return physiology.ReserveDrainPerSecond;
    }

    private void OnFuelMaterialUsed(Entity<NovakinFuelMaterialComponent> fuel, ref UseInHandEvent args)
    {
        if (args.Handled || !HasComp<NovakinPhysiologyComponent>(args.User)
            || !TryComp<NeedsComponent>(args.User, out var needs)
            || !needs.Needs.TryGetValue(NeedType.Fuel, out var coreFuel))
            return;
        if (coreFuel.CurrentValue >= coreFuel.MaxValue)
        {
            _popup.PopupEntity(Loc.GetString("novakin-fuel-full"), fuel, args.User);
            args.Handled = true;
            return;
        }
        if (!_stacks.Use(fuel.Owner, 1)) return;
        _needs.TryModifyNeedLevel(args.User, NeedType.Fuel, fuel.Comp.Fuel, needs);
        _popup.PopupEntity(Loc.GetString("novakin-fuel-consumed"), args.User, args.User);
        args.Handled = true;
    }

    private void OnNovakinHeat(ref ExecuteEntityEffectEvent<NovakinHeat> args)
    {
        if (!TryComp<NovakinPhysiologyComponent>(args.Args.TargetEntity, out var physiology)
            || !TryComp<TemperatureComponent>(args.Args.TargetEntity, out var temperature)) return;
        var delta = args.Effect.TemperatureDelta;
        if (args.Args is EntityEffectReagentArgs reagentArgs) delta *= reagentArgs.Scale.Float();
        if (delta > 0f)
            _temperature.ForceChangeTemperature(args.Args.TargetEntity, Math.Min(temperature.CurrentTemperature + delta, physiology.FuelConsumptionMaximumTemperature), temperature);
    }

    private void OnCryoPodInjection(Entity<NovakinPhysiologyComponent> entity, ref NovakinCryoPodInjectionEvent args)
    {
        foreach (var organ in _body.GetBodyOrgans(entity.Owner))
        {
            if (HasComp<StomachComponent>(organ.Id))
            {
                _stomach.TryTransferSolution(organ.Id, args.Solution);
                return;
            }
        }
    }

    private void UpdateThermalRegulation(EntityUid uid, NovakinPhysiologyComponent physiology, NeedsComponent needs)
    {
        if (!TryComp<ThermalRegulatorComponent>(uid, out var regulator)
            || !needs.Needs.TryGetValue(NeedType.Fuel, out var fuel))
            return;

        if (physiology.BaseImplicitHeatRegulation < 0f)
        {
            physiology.BaseImplicitHeatRegulation = regulator.ImplicitHeatRegulation;
            physiology.BaseSweatHeatRegulation = regulator.SweatHeatRegulation;
            physiology.BaseShiveringHeatRegulation = regulator.ShiveringHeatRegulation;
        }

        var fueled = fuel.CurrentValue > fuel.MinValue;
        regulator.ImplicitHeatRegulation = fueled ? physiology.BaseImplicitHeatRegulation : 0f;
        regulator.SweatHeatRegulation = fueled ? physiology.BaseSweatHeatRegulation : 0f;
        regulator.ShiveringHeatRegulation = fueled ? physiology.BaseShiveringHeatRegulation : 0f;
    }

    private void CoolWhenFuelDepleted(EntityUid uid, NovakinPhysiologyComponent physiology, NeedsComponent needs, TemperatureComponent temperature, float elapsed)
    {
        if (needs.Needs.TryGetValue(NeedType.Fuel, out var fuel) && fuel.CurrentValue <= fuel.MinValue)
            _temperature.ForceChangeTemperature(uid, temperature.CurrentTemperature - physiology.FuelDepletedCoolingPerSecond * elapsed, temperature);
    }

    private static void UpdateFuelConsumptionRate(NovakinPhysiologyComponent physiology, NeedsComponent needs, TemperatureComponent temperature)
    {
        if (!needs.Needs.TryGetValue(NeedType.Fuel, out var fuel)) return;
        if (physiology.BaseFuelDecayRate < 0f) physiology.BaseFuelDecayRate = fuel.DecayRate;
        var range = physiology.FuelConsumptionMaximumTemperature - physiology.FuelConsumptionBaselineTemperature;
        var hot = range > 0f ? Math.Clamp((temperature.CurrentTemperature - physiology.FuelConsumptionBaselineTemperature) / range, 0f, 1f) : 1f;
        fuel.DecayRate = physiology.BaseFuelDecayRate * MathHelper.Lerp(1f, physiology.MaximumFuelConsumptionMultiplier, hot);
    }

    private void UpdateHeatSpeed(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var range = physiology.FuelConsumptionMaximumTemperature - physiology.FuelConsumptionBaselineTemperature;
        var progress = range > 0f
            ? Math.Clamp((temperature.CurrentTemperature - physiology.FuelConsumptionBaselineTemperature) / range, 0f, 1f)
            : 1f;
        var multiplier = MathHelper.Lerp(1f, physiology.MaximumHeatSpeedMultiplier, progress);
        if (MathF.Abs(physiology.HeatSpeedMultiplier - multiplier) < 0.001f)
            return;

        physiology.HeatSpeedMultiplier = multiplier;
        Dirty(uid, physiology);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private static void OnRefreshMovement(Entity<NovakinPhysiologyComponent> entity,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(entity.Comp.HeatSpeedMultiplier, entity.Comp.HeatSpeedMultiplier);
    }
}
