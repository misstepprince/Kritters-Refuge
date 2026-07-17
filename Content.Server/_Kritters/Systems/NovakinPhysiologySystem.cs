using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._CS.Needs;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Systems;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.SSDIndicator;
using Content.Shared._Kritters.Overlays;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Server.GameObjects;

namespace Content.Server._Kritters.Systems;

/// <summary>Compact nitrogen reserve, core fuel, and thermal-shell physiology.</summary>
public sealed partial class NovakinPhysiologySystem : SharedNovakinPhysiologySystem
{
    private const float DangerousCold = 323.15f;
    private const float DangerousHeat = 700f;
    private const float ShellFailureDamage = 100f;

    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private StomachSystem _stomach = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private BarotraumaSystem _barotrauma = default!;
    [Dependency] private SharedNeedsSystem _needs = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NovakinPhysiologyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<NovakinPhysiologyComponent, NovakinCryoPodInjectionEvent>(OnCryoPodInjection);
        SubscribeLocalEvent<NovakinPhysiologyComponent, NovakinCoreFuelMetabolizedEvent>(OnFuelMetabolized);
    }

    public override void Update(float frameTime)
    {
        _accumulator = Math.Min(_accumulator + frameTime, 5f);
        if (_accumulator < 0.5f)
            return;

        var elapsed = MathF.Floor(_accumulator / 0.5f) * 0.5f;
        _accumulator -= elapsed;
        var query = EntityQueryEnumerator<NovakinPhysiologyComponent, NeedsComponent, TemperatureComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var physiology, out var needs, out var temperature, out var transform))
        {
            var remaining = elapsed;
            while (remaining > 0f)
            {
                var step = Math.Min(remaining, 0.5f);
                remaining -= step;
                UpdateThermalRegulation(uid, physiology, needs);
                CoolWhenFuelDepleted(uid, physiology, needs, temperature, step);
                UpdateShellState(uid, physiology);
                UpdateShellTemperatureTransfer(uid, physiology, temperature);
                ApplyThermalShellDamage(uid, physiology, temperature, step);
                UpdateShellState(uid, physiology);
                UpdateShellTemperatureTransfer(uid, physiology, temperature);
                DrainFuelForHeat(uid, physiology, needs, temperature, step);

                var lost = RemoveReserve((uid, physiology), GetReserveDrainRate(uid, physiology) * step);

                if (lost > 0f)
                    _atmosphere.GetTileMixture((uid, transform), excite: true)?.AdjustMoles(Gas.Nitrogen, lost * physiology.LeakedMolesPerReserve);

                // Dormancy reduces resource use, not the harm caused by an already-starved Core.
                ApplyGasBloodloss(uid, physiology, step,
                    GetReserveDrainRate(uid, physiology, applySsdMultiplier: false));
            }
            UpdateHeatSpeed(uid, physiology, temperature);
            UpdateColdSpeed(uid, physiology, temperature);
            UpdateGlow(uid, physiology, temperature);
            UpdateNightVision(uid, physiology, temperature);
            UpdateThermalWarning(uid, physiology, temperature);
            UpdateReserveAlert(uid, physiology);
        }
    }

    private void ApplyThermalShellDamage(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature, float elapsed)
    {
        if (!IsThermallyUnsafe(temperature) || !TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var scale = GetThermalSeverity(physiology, temperature);
        if (!physiology.ShellShattered)
        {
            var stress = Math.Min(physiology.ThermalStressDamagePerSecond * scale,
                physiology.MaximumThermalStressDamagePerSecond) * elapsed;
            _damageable.TryChangeDamage(uid, new DamageSpecifier { DamageDict = { ["Blunt"] = stress } },
                interruptsDoAfters: false, originFlag: DamageableSystem.DamageOriginFlag.Environmental);
            return;
        }

        var reserveFraction = GetReserveFraction(physiology);
        var gasAmplification = physiology.GasThermalDamagePerSecond
            * MathF.Pow(reserveFraction, physiology.GasThermalDamageExponent);
        var damage = (physiology.CompromisedShellThermalDamagePerSecond + gasAmplification) * scale * elapsed;
        var type = temperature.CurrentTemperature < DangerousCold ? "Cold" : "Heat";
        _damageable.TryChangeDamage(uid, new DamageSpecifier { DamageDict = { [type] = damage } },
            interruptsDoAfters: false, originFlag: DamageableSystem.DamageOriginFlag.Environmental);
    }

    private void UpdateShellState(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var shattered = GetBruteDamage(damageable) >= ShellFailureDamage;
        if (physiology.ShellShattered == shattered)
            return;

        physiology.ShellShattered = shattered;
        if (shattered)
            _audio.PlayPvs("/Audio/Effects/space_wind.ogg", uid, AudioParams.Default.WithVariation(0.125f));
        Dirty(uid, physiology);
    }

    private void UpdateShellTemperatureTransfer(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature)
    {
        if (physiology.BaseAtmosTemperatureTransferEfficiency < 0f)
            physiology.BaseAtmosTemperatureTransferEfficiency = temperature.AtmosTemperatureTransferEfficiency;

        var multiplier = physiology.ShellShattered
            ? physiology.ShellFailureTemperatureTransferMultiplier
            : 1f;
        if (physiology.ShellShattered && HasPressureSuit(uid))
            multiplier *= physiology.PressureSuitShellFailureTemperatureTransferMultiplier;

        temperature.AtmosTemperatureTransferEfficiency = physiology.BaseAtmosTemperatureTransferEfficiency * multiplier;
    }

    private void ApplyGasBloodloss(EntityUid uid, NovakinPhysiologyComponent physiology, float elapsed,
        float actualDrainRate)
    {
        var fraction = GetReserveFraction(physiology);
        if (fraction < physiology.BloodlossReserveThreshold)
        {
            var severity = 1f - fraction / physiology.BloodlossReserveThreshold;
            var drainScale = fraction <= 0f
                ? 1f
                : actualDrainRate / GetUnprotectedReserveDrainRate(physiology);
            _damageable.TryChangeDamage(uid, physiology.BloodlossDamage * (severity * drainScale * elapsed),
                interruptsDoAfters: false);
        }
        else
        {
            _damageable.TryChangeDamage(uid, physiology.BloodlossHealDamage * (fraction * elapsed),
                ignoreResistances: true, interruptsDoAfters: false);
        }
    }

    private float GetReserveDrainRate(EntityUid uid, NovakinPhysiologyComponent physiology,
        bool applySsdMultiplier = true)
    {
        var rate = GetUnprotectedReserveDrainRate(physiology);
        if (HasPressureSuit(uid))
            rate *= physiology.ShellShattered
                ? physiology.PressureSuitShellFailureReserveDrainMultiplier
                : physiology.PressureSuitReserveDrainMultiplier;
        if (applySsdMultiplier && IsPlayerSsd(uid) && _mobState.IsAlive(uid))
            rate *= physiology.SsdReserveDrainMultiplier;
        return rate;
    }

    private static float GetUnprotectedReserveDrainRate(NovakinPhysiologyComponent physiology)
        => physiology.ShellShattered
            ? physiology.ShellFailureReserveDrainPerSecond
            : physiology.ReserveDrainPerSecond;

    private bool HasPressureSuit(EntityUid uid)
        => _inventory.TryGetSlotEntity(uid, "outerClothing", out var outer)
           && _barotrauma.TryGetPressureProtectionValues((outer.Value, CompOrNull<PressureProtectionComponent>(outer.Value)), out _, out _, out _, out _);

    private float GetHeatResourceMultiplier(NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var range = physiology.MaximumHeatSpeedTemperature - physiology.FuelConsumptionBaselineTemperature;
        var progress = range > 0f ? Math.Clamp((temperature.CurrentTemperature - physiology.FuelConsumptionBaselineTemperature) / range, 0f, 1f) : 1f;
        return MathHelper.Lerp(1f, physiology.MaximumHeatResourceDrainMultiplier, progress);
    }

    private void DrainFuelForHeat(EntityUid uid, NovakinPhysiologyComponent physiology, NeedsComponent needs, TemperatureComponent temperature, float elapsed)
    {
        if (!needs.Needs.TryGetValue(NeedType.Fuel, out var fuel))
            return;
        var extra = fuel.DecayRate * (GetHeatResourceMultiplier(physiology, temperature) - 1f) * elapsed;
        if (extra > 0f)
            _needs.TryModifyNeedLevel(uid, NeedType.Fuel, -extra, needs);
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

    private void UpdateHeatSpeed(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var range = physiology.MaximumHeatSpeedTemperature - physiology.FuelConsumptionBaselineTemperature;
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

    private void UpdateColdSpeed(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var current = temperature.CurrentTemperature;
        float multiplier;
        if (current >= physiology.ColdSpeedStartTemperature)
        {
            multiplier = 1f;
        }
        else if (current >= physiology.ColdSpeedDangerTemperature)
        {
            var progress = (physiology.ColdSpeedStartTemperature - current)
                / Math.Max(physiology.ColdSpeedStartTemperature - physiology.ColdSpeedDangerTemperature, 1f);
            multiplier = MathHelper.Lerp(1f, physiology.ColdSpeedAtDangerTemperature, progress);
        }
        else
        {
            var progress = (physiology.ColdSpeedDangerTemperature - current)
                / Math.Max(physiology.ColdSpeedDangerTemperature - physiology.ColdSpeedMinimumTemperature, 1f);
            multiplier = MathHelper.Lerp(physiology.ColdSpeedAtDangerTemperature,
                physiology.MinimumColdSpeedMultiplier, Math.Clamp(progress, 0f, 1f));
        }

        if (MathF.Abs(physiology.ColdSpeedMultiplier - multiplier) < 0.001f)
            return;

        physiology.ColdSpeedMultiplier = multiplier;
        Dirty(uid, physiology);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void UpdateGlow(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var range = DangerousHeat - DangerousCold;
        var progress = range > 0f
            ? Math.Clamp((temperature.CurrentTemperature - DangerousCold) / range, 0f, 1f)
            : 1f;
        if (MathF.Abs(physiology.GlowIntensity - progress) > 0.01f)
        {
            physiology.GlowIntensity = progress;
            Dirty(uid, physiology);
        }
        _lights.SetRadius(uid, MathHelper.Lerp(0.5f, 3f, progress));
        _lights.SetEnergy(uid, MathHelper.Lerp(0.35f, 1.5f, progress));
    }

    private static void OnRefreshMovement(Entity<NovakinPhysiologyComponent> entity,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        var multiplier = entity.Comp.HeatSpeedMultiplier * entity.Comp.ReserveSpeedMultiplier;
        multiplier *= entity.Comp.ColdSpeedMultiplier;
        args.ModifySpeed(multiplier, multiplier);
    }

    private void UpdateThermalWarning(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var current = temperature.CurrentTemperature;
        physiology.ThermalWarningHot = current >= 600f || physiology.ThermalWarningHot && current >= 590f;
        physiology.ThermalWarningCold = current <= 340f || physiology.ThermalWarningCold && current <= 350f;
        if (physiology.ThermalWarningHot)
            _alerts.ShowAlert(uid, "NovakinTemperature", (short) (current >= DangerousHeat ? 6 : current >= 650f ? 5 : 4));
        else if (physiology.ThermalWarningCold)
            _alerts.ShowAlert(uid, "NovakinTemperature", (short) (current < DangerousCold ? 3 : current < 330f ? 2 : 1));
        else
            _alerts.ClearAlertCategory(uid, "Temperature");
        Dirty(uid, physiology);
    }

    private void OnFuelMetabolized(Entity<NovakinPhysiologyComponent> entity, ref NovakinCoreFuelMetabolizedEvent args)
    {
        if (args.Heat <= 0f || !TryComp<TemperatureComponent>(entity, out var temperature))
            return;

        var updatedTemperature = temperature.CurrentTemperature + args.Heat;
        if (!args.AllowOverheat)
            updatedTemperature = Math.Min(updatedTemperature, DangerousHeat);
        _temperature.ForceChangeTemperature(entity, updatedTemperature, temperature);
    }

    private static bool IsThermallyUnsafe(TemperatureComponent temperature)
        => temperature.CurrentTemperature < DangerousCold || temperature.CurrentTemperature >= DangerousHeat;

    private static float GetThermalSeverity(NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        var excess = temperature.CurrentTemperature < DangerousCold
            ? DangerousCold - temperature.CurrentTemperature
            : temperature.CurrentTemperature - DangerousHeat;
        return 1f + Math.Clamp(excess / Math.Max(physiology.ThermalDamageScaleRange, 1f), 0f, 1f);
    }

    private static float GetReserveFraction(NovakinPhysiologyComponent physiology)
        => physiology.MaxReserve <= 0f
            ? 0f
            : Math.Clamp(physiology.CurrentReserve / physiology.MaxReserve, 0f, 1f);

    private bool IsPlayerSsd(EntityUid uid)
        => TryComp<SSDIndicatorComponent>(uid, out var ssd) && ssd.IsSSD
           && TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind;

    private void UpdateNightVision(EntityUid uid, NovakinPhysiologyComponent physiology, TemperatureComponent temperature)
    {
        if (!TryComp<KrittersNightVisionComponent>(uid, out var vision))
            return;

        var current = temperature.CurrentTemperature;
        var illumination = Math.Clamp((current - physiology.ColdSpeedMinimumTemperature)
            / (physiology.FuelConsumptionBaselineTemperature - physiology.ColdSpeedMinimumTemperature), 0f, 1f);
        var saturation = Math.Clamp((current - physiology.FuelConsumptionBaselineTemperature)
            / (DangerousHeat - physiology.FuelConsumptionBaselineTemperature), 0f, 1f);
        // Retain useful vision at dangerous temperatures instead of turning the viewport white.
        var washout = saturation * 0.2125f;
        if (MathF.Abs(vision.Illumination - illumination) < 0.001f
            && MathF.Abs(vision.HeatSaturation - saturation) < 0.001f
            && MathF.Abs(vision.HeatWashout - washout) < 0.001f)
            return;

        vision.Illumination = illumination;
        vision.HeatSaturation = saturation;
        vision.HeatWashout = washout;
        Dirty(uid, vision);
    }

    private void UpdateReserveAlert(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        var fraction = GetReserveFraction(physiology);
        var speed = fraction < physiology.BloodlossReserveThreshold
            ? MathHelper.Lerp(physiology.LowReserveMinimumSpeedMultiplier, 1f, fraction / physiology.BloodlossReserveThreshold)
            : 1f;
        if (MathF.Abs(speed - physiology.ReserveSpeedMultiplier) > 0.001f)
        {
            physiology.ReserveSpeedMultiplier = speed;
            _movement.RefreshMovementSpeedModifiers(uid);
        }
        _alerts.ShowAlert(uid, "NovakinGasReserve", (short) Math.Clamp((int) MathF.Ceiling(fraction * 10f), 0, 10));
    }

    private static float GetBruteDamage(DamageableComponent damageable)
        => damageable.Damage.DamageDict.GetValueOrDefault("Blunt").Float()
           + damageable.Damage.DamageDict.GetValueOrDefault("Slash").Float()
           + damageable.Damage.DamageDict.GetValueOrDefault("Piercing").Float();
}
