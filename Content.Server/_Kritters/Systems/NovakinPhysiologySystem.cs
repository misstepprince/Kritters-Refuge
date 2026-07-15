using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Stack;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.EntityEffects;
using Content.Shared._Kritters;
using Content.Shared._Kritters.Systems;
using Content.Shared._CS.Needs;
using Content.Shared.Alert;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;

namespace Content.Server._Kritters.Systems;

public sealed partial class NovakinPhysiologySystem : SharedNovakinPhysiologySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedNeedsSystem _needs = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private PointLightSystem _pointLight = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private TemperatureSystem _temperature = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentShutdown>(OnPhysiologyShutdown);
        SubscribeLocalEvent<NovakinFuelMaterialComponent, UseInHandEvent>(OnFuelMaterialUsed);
        SubscribeLocalEvent<NovakinPhysiologyComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<NovakinPhysiologyComponent, NovakinDischargeEvent>(OnDischarge);
        SubscribeLocalEvent<NovakinPhysiologyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<ExecuteEntityEffectEvent<NovakinHeat>>(OnNovakinHeat);
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
            UpdateHeatSpeed(uid, physiology, temperature);
            CoolWhenFuelDepleted(uid, physiology, needs, temperature, elapsed);

            var reserveLost = 0f;
            if (!_mobState.IsDead(uid, mobState) && !HasComp<CryostorageContainedComponent>(uid))
                reserveLost = RemoveReserve((uid, physiology), GetReserveDrainRate(uid, physiology, mobState) * elapsed);

            UpdateReserveAlert(uid, physiology);
            UpdateGlow(uid, physiology, temperature, mobState);
            UpdateReserveConsequences(uid, physiology, transform, mobState, reserveLost, elapsed);

            // Native ThermalRegulator and worn TemperatureProtection handle self-heating and insulation.
            // This Novakin-specific step deposits the heat lost by their gaseous body into the room.
            if (temperature.CurrentTemperature < physiology.LastTemperature)
            {
                var mixture = _atmosphere.GetTileMixture((uid, transform), excite: true);
                if (mixture != null)
                {
                    var lostHeat = (physiology.LastTemperature - temperature.CurrentTemperature)
                        * _temperature.GetHeatCapacity(uid, temperature);
                    _atmosphere.AddHeat(mixture, lostHeat);
                }
            }

            physiology.LastTemperature = temperature.CurrentTemperature;
        }
    }

    private void OnFuelMaterialUsed(Entity<NovakinFuelMaterialComponent> fuel, ref UseInHandEvent args)
    {
        if (args.Handled
            || !HasComp<NovakinPhysiologyComponent>(args.User)
            || !TryComp<NeedsComponent>(args.User, out var needs)
            || !needs.Needs.TryGetValue(NeedType.Fuel, out var coreFuel))
            return;

        if (coreFuel.CurrentValue >= coreFuel.MaxValue)
        {
            _popup.PopupEntity(Loc.GetString("novakin-fuel-full"), fuel, args.User);
            args.Handled = true;
            return;
        }

        if (!_stacks.Use(fuel.Owner, 1))
            return;

        _needs.TryModifyNeedLevel(args.User, NeedType.Fuel, fuel.Comp.Fuel, needs);
        _popup.PopupEntity(Loc.GetString("novakin-fuel-consumed"), args.User, args.User);

        args.Handled = true;
    }

    private void OnNovakinHeat(ref ExecuteEntityEffectEvent<NovakinHeat> args)
    {
        if (!TryComp<NovakinPhysiologyComponent>(args.Args.TargetEntity, out var physiology)
            || !TryComp<TemperatureComponent>(args.Args.TargetEntity, out var temperature))
            return;

        var delta = args.Effect.TemperatureDelta;
        if (args.Args is EntityEffectReagentArgs reagentArgs)
            delta *= reagentArgs.Scale.Float();

        if (delta <= 0f)
            return;

        _temperature.ForceChangeTemperature(
            args.Args.TargetEntity,
            Math.Min(temperature.CurrentTemperature + delta, physiology.FuelConsumptionMaximumTemperature),
            temperature);
    }

    private void CoolWhenFuelDepleted(
        EntityUid uid,
        NovakinPhysiologyComponent physiology,
        NeedsComponent needs,
        TemperatureComponent temperature,
        float elapsed)
    {
        if (needs.Needs.TryGetValue(NeedType.Fuel, out var fuel)
            && fuel.CurrentValue <= fuel.MinValue
            && physiology.FuelDepletedCoolingPerSecond > 0f)
        {
            _temperature.ForceChangeTemperature(uid,
                temperature.CurrentTemperature - physiology.FuelDepletedCoolingPerSecond * elapsed,
                temperature);
        }
    }

    private static void UpdateFuelConsumptionRate(
        NovakinPhysiologyComponent physiology,
        NeedsComponent needs,
        TemperatureComponent temperature)
    {
        if (!needs.Needs.TryGetValue(NeedType.Fuel, out var fuel))
            return;

        if (physiology.BaseFuelDecayRate < 0f)
            physiology.BaseFuelDecayRate = fuel.DecayRate;

        var temperatureRange = physiology.FuelConsumptionMaximumTemperature - physiology.FuelConsumptionBaselineTemperature;
        var temperatureFactor = temperatureRange > 0f
            ? Math.Clamp(
                (temperature.CurrentTemperature - physiology.FuelConsumptionBaselineTemperature) / temperatureRange,
                0f,
                1f)
            : 1f;
        var multiplier = MathHelper.Lerp(1f, physiology.MaximumFuelConsumptionMultiplier, temperatureFactor);
        fuel.DecayRate = physiology.BaseFuelDecayRate * multiplier;
    }

    private void OnDamageModify(Entity<NovakinPhysiologyComponent> entity, ref DamageModifyEvent args)
    {
        if (!TryComp<TemperatureComponent>(entity, out var temperature))
            return;

        var range = entity.Comp.FuelConsumptionMaximumTemperature - entity.Comp.FuelConsumptionBaselineTemperature;
        var factor = range > 0f
            ? Math.Clamp((temperature.CurrentTemperature - entity.Comp.FuelConsumptionBaselineTemperature) / range, 0f, 1f)
            : 1f;

        if (args.Damage.DamageDict.TryGetValue("Blunt", out var blunt))
            args.Damage.DamageDict["Blunt"] = blunt * (1f - 0.25f * factor);

        if (args.Damage.DamageDict.TryGetValue("Cold", out var cold))
            args.Damage.DamageDict["Cold"] = cold * (1f + 0.5f * factor);
    }

    private void OnRefreshMovement(Entity<NovakinPhysiologyComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(entity.Comp.HeatSpeedMultiplier, entity.Comp.HeatSpeedMultiplier);
    }

    private void OnDischarge(Entity<NovakinPhysiologyComponent> entity, ref NovakinDischargeEvent args)
    {
        if (args.Handled || !TryComp<TemperatureComponent>(entity, out var temperature) || temperature.CurrentTemperature < 650f)
            return;

        var removed = RemoveReserve(entity, entity.Comp.CurrentReserve * 0.5f);
        if (removed > 0f && TryComp<TransformComponent>(entity, out var transform)
            && Prototypes.TryIndex(entity.Comp.Gas, out var gas))
            _atmosphere.GetTileMixture((entity, transform), excite: true)?.AdjustMoles(gas.Gas, removed * entity.Comp.LeakedMolesPerReserve);

        _temperature.ForceChangeTemperature(entity, temperature.CurrentTemperature - 300f, temperature);
        args.Handled = true;
    }

    private void UpdateHeatSpeed(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var range = physiology.FuelConsumptionMaximumTemperature - physiology.FuelConsumptionBaselineTemperature;
        var factor = range > 0f
            ? Math.Clamp((temperature.CurrentTemperature - physiology.FuelConsumptionBaselineTemperature) / range, 0f, 1f)
            : 1f;
        var multiplier = MathHelper.Lerp(1f, physiology.MaximumHeatSpeedMultiplier, factor);
        if (MathF.Abs(physiology.HeatSpeedMultiplier - multiplier) < 0.001f)
            return;

        physiology.HeatSpeedMultiplier = multiplier;
        Dirty(uid, physiology);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void UpdateReserveConsequences(
        EntityUid uid,
        NovakinPhysiologyComponent physiology,
        TransformComponent transform,
        MobStateComponent mobState,
        float reserveLost,
        float elapsed)
    {
        if (reserveLost > 0f
            && physiology.LeakedMolesPerReserve > 0f
            && Prototypes.TryIndex(physiology.Gas, out var gasPrototype))
        {
            var mixture = _atmosphere.GetTileMixture((uid, transform), excite: true);
            mixture?.AdjustMoles(gasPrototype.Gas, reserveLost * physiology.LeakedMolesPerReserve * GetLeakMultiplier(uid, physiology));
        }

        var fraction = physiology.MaxReserve > 0f
            ? Math.Clamp(physiology.CurrentReserve / physiology.MaxReserve, 0f, 1f)
            : 0f;

        if (TryComp<ThermalRegulatorComponent>(uid, out var regulator))
        {
            if (physiology.BaseImplicitHeatRegulation < 0f)
            {
                physiology.BaseImplicitHeatRegulation = regulator.ImplicitHeatRegulation;
                physiology.BaseSweatHeatRegulation = regulator.SweatHeatRegulation;
                physiology.BaseShiveringHeatRegulation = regulator.ShiveringHeatRegulation;
            }

            var multiplier = GetThermalRegulationMultiplier(physiology, fraction) * GetHeatFactor(uid, physiology, 0.5f);
            regulator.ImplicitHeatRegulation = physiology.BaseImplicitHeatRegulation * multiplier;
            regulator.SweatHeatRegulation = physiology.BaseSweatHeatRegulation * multiplier;
            regulator.ShiveringHeatRegulation = physiology.BaseShiveringHeatRegulation * multiplier;
        }

        if (_mobState.IsDead(uid, mobState)
            || physiology.DamageThreshold <= 0f
            || fraction >= physiology.DamageThreshold)
        {
            return;
        }

        var severity = 1f - fraction / physiology.DamageThreshold;
        var damage = physiology.EmptyReserveDamagePerSecond * severity * elapsed;
        if (damage <= 0f)
            return;

        _damageable.TryChangeDamage(uid, new DamageSpecifier
        {
            DamageDict = { ["Cellular"] = damage },
        }, interruptsDoAfters: false);
    }

    private void OnPhysiologyShutdown(
        Entity<NovakinPhysiologyComponent> entity,
        ref ComponentShutdown args)
    {
        _alerts.ClearAlert(entity, entity.Comp.ReserveAlert);
    }

    private void UpdateReserveAlert(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        var fraction = physiology.MaxReserve > 0f
            ? Math.Clamp(physiology.CurrentReserve / physiology.MaxReserve, 0f, 1f)
            : 0f;

        // Reserve zero is reserved for a truly empty body; any remaining gas
        // displays at least one segment so the gauge does not imply depletion early.
        var severity = fraction <= 0f
            ? (short) 0
            : (short) Math.Max(1, MathF.Round(fraction * 10f));
        _alerts.ShowAlert(uid, physiology.ReserveAlert, severity);
    }

    private void UpdateGlow(
        EntityUid uid,
        NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature,
        MobStateComponent mobState)
    {
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
            || !TryComp<PointLightComponent>(uid, out var light))
        {
            return;
        }

        var temperatureRange = physiology.FuelConsumptionMaximumTemperature - physiology.MinimumGlowTemperature;
        var temperatureFactor = temperatureRange > 0f
            ? Math.Clamp(
                (temperature.CurrentTemperature - physiology.MinimumGlowTemperature) / temperatureRange,
                0f,
                1f)
            : 1f;

        var dead = _mobState.IsDead(uid, mobState);
        var intensity = dead
            ? physiology.DeadGlowEnergy
            : MathHelper.Lerp(physiology.MinimumGlowEnergy, physiology.FullGlowEnergy, temperatureFactor);

        // A pressure suit and pressure helmet form a sealed shell that blocks the emitted light.
        if (HasSealedPressureSuit(uid))
            intensity = 0f;

        _pointLight.SetColor(uid, humanoid.SkinColor, light);
        _pointLight.SetEnergy(uid, intensity, light);

        var normalizedIntensity = physiology.FullGlowEnergy > 0f
            ? Math.Clamp(intensity / physiology.FullGlowEnergy, 0f, 1f)
            : 0f;
        if (!MathHelper.CloseToPercent(physiology.GlowIntensity, normalizedIntensity))
        {
            physiology.GlowIntensity = normalizedIntensity;
            Dirty(uid, physiology);
        }
    }

    private float GetLeakMultiplier(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        return HasPressureProtectionInSlot(uid, "outerClothing")
            ? Math.Clamp(physiology.PressureSuitLeakMultiplier, 0f, 1f)
            : 1f;
    }

    private static float GetThermalRegulationMultiplier(NovakinPhysiologyComponent physiology, float fraction)
    {
        var threshold = Math.Clamp(physiology.CriticalReserveThreshold, 0.01f, 1f);
        if (fraction >= threshold)
        {
            var stableFraction = (fraction - threshold) / (1f - threshold);
            return MathHelper.Lerp(
                physiology.CriticalReserveThermalRegulationMultiplier,
                1f,
                stableFraction);
        }

        var collapseFraction = 1f - fraction / threshold;
        var collapseSeverity = MathF.Pow(collapseFraction,
            Math.Max(0.01f, physiology.CriticalReserveThermalRegulationExponent));
        return MathHelper.Lerp(
            physiology.CriticalReserveThermalRegulationMultiplier,
            physiology.EmptyReserveThermalRegulationMultiplier,
            collapseSeverity);
    }

    private float GetReserveDrainRate(EntityUid uid, NovakinPhysiologyComponent physiology, MobStateComponent mobState)
    {
        var fraction = physiology.MaxReserve > 0f
            ? Math.Clamp(physiology.CurrentReserve / physiology.MaxReserve, 0f, 1f)
            : 0f;
        var multiplier = MathHelper.Lerp(
            physiology.FullReserveDrainMultiplier,
            physiology.EmptyReserveDrainMultiplier,
            1f - fraction);

        if (physiology.CriticalReserveThreshold > 0f && fraction < physiology.CriticalReserveThreshold)
        {
            var criticalFraction = 1f - fraction / physiology.CriticalReserveThreshold;
            var criticalSeverity = MathF.Pow(criticalFraction,
                Math.Max(0.01f, physiology.CriticalReserveDrainExponent));
            multiplier += (physiology.CriticalReserveDrainMultiplier - physiology.EmptyReserveDrainMultiplier)
                * criticalSeverity;
        }

        if (_mobState.IsCritical(uid, mobState))
            multiplier *= Math.Max(0f, physiology.CriticalHealthDrainMultiplier);

        return physiology.ReserveDrainPerSecond * Math.Max(0f, multiplier) * GetHeatFactor(uid, physiology, 0.5f);
    }

    private float GetHeatFactor(EntityUid uid, NovakinPhysiologyComponent physiology, float minimum)
    {
        if (!TryComp<TemperatureComponent>(uid, out var temperature))
            return 1f;
        var range = physiology.FuelConsumptionMaximumTemperature - physiology.FuelConsumptionBaselineTemperature;
        var progress = range > 0f
            ? Math.Clamp((temperature.CurrentTemperature - physiology.FuelConsumptionBaselineTemperature) / range, 0f, 1f)
            : 1f;
        return MathHelper.Lerp(1f, minimum, progress);
    }

    private bool HasSealedPressureSuit(EntityUid uid)
    {
        return HasPressureProtectionInSlot(uid, "outerClothing")
            && HasPressureProtectionInSlot(uid, "head");
    }

    private bool HasPressureProtectionInSlot(EntityUid uid, string slot)
    {
        return _inventory.TryGetSlotEntity(uid, slot, out var item)
            && HasComp<PressureProtectionComponent>(item);
    }
}
