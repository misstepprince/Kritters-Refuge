using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Kritters.Novakin.Components;
using Content.Shared._Kritters.Novakin.Systems;
using Content.Shared.Alert;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;

namespace Content.Server._Kritters.Novakin.Systems;

public sealed class NovakinPhysiologySystem : SharedNovakinPhysiologySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PointLightSystem _pointLight = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentShutdown>(OnPhysiologyShutdown);
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < 0.5f)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<NovakinPhysiologyComponent, TemperatureComponent, TransformComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var physiology, out var temperature, out var transform, out var mobState))
        {
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

            var multiplier = GetThermalRegulationMultiplier(physiology, fraction);
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

        var temperatureRange = physiology.FullGlowTemperature - physiology.MinimumGlowTemperature;
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
        var threshold = Math.Clamp(physiology.DamageThreshold, 0.01f, 1f);
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

        return physiology.ReserveDrainPerSecond * Math.Max(0f, multiplier);
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
