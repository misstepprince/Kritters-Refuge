using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Server.Speech.Components;
using Content.Shared._CS.Needs;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Systems;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffect;
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
    private const float StefanBoltzmannConstant = 5.670374419e-8f;
    private const string SlurredSpeechKey = "SlurredSpeech";

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
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NovakinPhysiologyComponent, NovakinCryoPodInjectionEvent>(OnCryoPodInjection);
        SubscribeLocalEvent<NovakinPhysiologyComponent, NovakinCoreCoolingEvent>(OnCoreCooling);
        SubscribeLocalEvent<NovakinPhysiologyComponent, ReagentMetabolizedEvent>(OnReagentMetabolized);
        SubscribeLocalEvent<NovakinPhysiologyComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<NovakinPhysiologyComponent, NeedExamineInfoEvent>(OnNeedExamineInfo);
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentStartup>(OnPhysiologyStartup);
    }

    public override void Update(float frameTime)
    {
        _accumulator = Math.Min(_accumulator + frameTime, 5f);
        if (_accumulator < 0.5f)
            return;

        var elapsed = MathF.Floor(_accumulator / 0.5f) * 0.5f;
        _accumulator -= elapsed;

        var environmentalQuery = EntityQueryEnumerator<NovakinPhysiologyComponent, TemperatureComponent>();
        while (environmentalQuery.MoveNext(out var uid, out var physiology, out var temperature))
        {
            if (HasComp<NeedsComponent>(uid))
                continue;

            var remaining = elapsed;
            while (remaining > 0f)
            {
                var step = Math.Min(remaining, 0.5f);
                remaining -= step;
                ApplyEnvironmentalTemperatureExchange(uid, physiology, temperature, step);
            }
        }

        var query = EntityQueryEnumerator<NovakinPhysiologyComponent, NeedsComponent, TemperatureComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var physiology, out var needs, out var temperature, out var transform))
        {
            var remaining = elapsed;
            while (remaining > 0f)
            {
                var step = Math.Min(remaining, 0.5f);
                remaining -= step;
                ApplyPendingReagentHeat(uid, physiology, temperature, step);
                ApplyEnvironmentalTemperatureExchange(uid, physiology, temperature, step);
                UpdateThermalRegulation(uid, physiology, needs, temperature);
                CoolWhenFuelDepleted(uid, physiology, needs, temperature, step);
                UpdateShellState(uid, physiology);
                UpdateShellTemperatureTransfer(physiology, temperature);
                ApplyThermalShellDamage(uid, physiology, temperature, step);
                UpdateShellState(uid, physiology);
                UpdateShellTemperatureTransfer(physiology, temperature);
                DrainFuelForHeat(uid, physiology, needs, temperature, step);

                var lost = RemoveReserve((uid, physiology), GetReserveDrainRate(uid, physiology) * step);

                if (lost > 0f)
                    _atmosphere.GetTileMixture((uid, transform), excite: true)?.AdjustMoles(Gas.Nitrogen, lost * physiology.LeakedMolesPerReserve);

                // Dormancy reduces resource use, not the harm caused by an already-starved Core.
                ApplyGasBloodloss(uid, physiology, step,
                    GetReserveDrainRate(uid, physiology, applySsdMultiplier: false));
                UpdateHeatIntoxication(uid, physiology, temperature);
                physiology.FlammableRegulationSuppressionRemaining = Math.Max(0f,
                    physiology.FlammableRegulationSuppressionRemaining - step);
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

    private static void UpdateShellTemperatureTransfer(NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature)
    {
        if (physiology.BaseAtmosTemperatureTransferEfficiency < 0f)
            physiology.BaseAtmosTemperatureTransferEfficiency = temperature.AtmosTemperatureTransferEfficiency;

        // Novakin environmental exchange is handled below without transferring heat into the gas.
        temperature.AtmosTemperatureTransferEfficiency = 0f;
    }

    private void OnPhysiologyStartup(Entity<NovakinPhysiologyComponent> entity, ref ComponentStartup args)
    {
        if (!TryComp<TemperatureComponent>(entity, out var temperature))
            return;

        entity.Comp.BaseAtmosTemperatureTransferEfficiency = temperature.AtmosTemperatureTransferEfficiency;
        temperature.AtmosTemperatureTransferEfficiency = 0f;
    }

    private void ApplyEnvironmentalTemperatureExchange(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature, float elapsed)
    {
        var mixture = _atmosphere.GetContainingMixture(uid);
        if (mixture == null || elapsed <= 0f)
            return;

        if (physiology.BaseAtmosTemperatureTransferEfficiency < 0f)
            physiology.BaseAtmosTemperatureTransferEfficiency = temperature.AtmosTemperatureTransferEfficiency;
        temperature.AtmosTemperatureTransferEfficiency = 0f;

        var bodyTemperature = temperature.CurrentTemperature;
        var bodyHeatCapacity = _temperature.GetHeatCapacity(uid, temperature);
        if (!float.IsFinite(bodyTemperature) || !float.IsFinite(bodyHeatCapacity) || bodyHeatCapacity <= 0f)
            return;

        var transferMultiplier = physiology.ShellShattered
            ? physiology.ShellFailureTemperatureTransferMultiplier
            : 1f;
        if (physiology.ShellShattered && HasPressureSuit(uid))
            transferMultiplier *= physiology.PressureSuitShellFailureTemperatureTransferMultiplier;

        var gasWeight = GetGasWeight(physiology, mixture);
        var gasHeatCapacity = mixture.Immutable && mixture.TotalMoles <= Atmospherics.GasMinMoles
            ? 0f
            : _atmosphere.GetHeatCapacity(mixture, false);
        var convectionHeat = 0f;
        if (gasHeatCapacity > 0f && float.IsFinite(gasHeatCapacity) && float.IsFinite(mixture.Temperature))
        {
            var combinedHeatCapacity = gasHeatCapacity + bodyHeatCapacity;
            convectionHeat = (mixture.Temperature - bodyTemperature)
                * gasHeatCapacity * bodyHeatCapacity / combinedHeatCapacity
                * physiology.BaseAtmosTemperatureTransferEfficiency * transferMultiplier * elapsed;
        }

        var radiationHeat = 0f;
        if (bodyTemperature > Atmospherics.TCMB)
        {
            var bodyTemperatureSquared = bodyTemperature * bodyTemperature;
            var cmbSquared = Atmospherics.TCMB * Atmospherics.TCMB;
            radiationHeat = -StefanBoltzmannConstant * physiology.RadiativeEmissivity
                * physiology.RadiativeSurfaceArea
                * (bodyTemperatureSquared * bodyTemperatureSquared - cmbSquared * cmbSquared)
                * transferMultiplier * elapsed;
        }

        var heat = convectionHeat * gasWeight + radiationHeat * (1f - gasWeight);
        var targetTemperature = MathHelper.Lerp(Atmospherics.TCMB, mixture.Temperature, gasWeight);
        var heatToTarget = (targetTemperature - bodyTemperature) * bodyHeatCapacity;
        var maximumHeat = Math.Max(physiology.MaximumEnvironmentalTemperatureChangePerSecond, 0f)
            * bodyHeatCapacity * elapsed;
        heat = Math.Clamp(heat, -maximumHeat, maximumHeat);
        heat = heatToTarget < 0f
            ? Math.Max(heat, heatToTarget)
            : Math.Min(heat, heatToTarget);

        if (float.IsFinite(heat) && heat != 0f)
            _temperature.ChangeHeat(uid, heat, temperature: temperature);
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
                args.Accepted = _stomach.TryTransferSolution(organ.Id, args.Solution);
                return;
            }
        }
    }

    private void UpdateThermalRegulation(EntityUid uid, NovakinPhysiologyComponent physiology, NeedsComponent needs,
        TemperatureComponent temperature)
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

        // Kritters: an active flammable Core reaction briefly overpowers ordinary temperature regulation.
        var canRegulate = fuel.CurrentValue > fuel.MinValue
            && physiology.FlammableRegulationSuppressionRemaining <= 0f;
        var gasWeight = _atmosphere.GetContainingMixture(uid) is { } mixture
            ? GetGasWeight(physiology, mixture)
            : 0f;
        // Thin atmosphere must not let Core warming cancel radiative vacuum cooling.
        var warmingMultiplier = temperature.CurrentTemperature < regulator.NormalBodyTemperature ? gasWeight : 1f;
        regulator.ImplicitHeatRegulation = canRegulate
            ? physiology.BaseImplicitHeatRegulation * warmingMultiplier
            : 0f;
        regulator.SweatHeatRegulation = canRegulate ? physiology.BaseSweatHeatRegulation : 0f;
        regulator.ShiveringHeatRegulation = canRegulate
            ? physiology.BaseShiveringHeatRegulation * gasWeight
            : 0f;
    }

    private static float GetGasWeight(NovakinPhysiologyComponent physiology, GasMixture mixture)
    {
        var molarDensity = mixture.Volume > 0f ? mixture.TotalMoles / mixture.Volume : 0f;
        return Math.Clamp(molarDensity
            / Math.Max(physiology.FullConvectionMoleDensity, float.Epsilon), 0f, 1f);
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
        if (TryComp<HumanoidAppearanceComponent>(uid, out var appearance))
            _lights.SetColor(uid, appearance.SkinColor);
        _lights.SetRadius(uid, MathHelper.Lerp(0.5f, 3f, progress));
        _lights.SetEnergy(uid, MathHelper.Lerp(0.35f, 1.5f, progress));
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

    private void OnCoreCooling(Entity<NovakinPhysiologyComponent> entity, ref NovakinCoreCoolingEvent args)
    {
        if (args.TemperatureDelta <= 0f || !TryComp<TemperatureComponent>(entity, out var temperature))
            return;

        var updatedTemperature = Math.Max(temperature.CurrentTemperature - args.TemperatureDelta,
            Math.Min(temperature.CurrentTemperature, DangerousCold));
        _temperature.ForceChangeTemperature(entity, updatedTemperature, temperature);
    }

    private void OnNeedExamineInfo(Entity<NovakinPhysiologyComponent> entity, ref NeedExamineInfoEvent args)
    {
        if (args.Need.NeedType != NeedType.Fuel)
            return;

        // Kritters: Fuel and shell nitrogen are the Core's paired survival resources.
        var reserve = entity.Comp.MaxReserve > 0f
            ? Math.Clamp(entity.Comp.CurrentReserve / entity.Comp.MaxReserve, 0f, 1f)
            : 0f;
        args.AdditionalInfoLines.Add(Loc.GetString("examinable-need-novakin-nitrogen-reserve",
            ("percent", (int) MathF.Round(reserve * 100f))));
    }

    private void OnReagentMetabolized(Entity<NovakinPhysiologyComponent> entity,
        ref ReagentMetabolizedEvent args)
    {
        if (!_mobState.IsAlive(entity)
            || args.Reagent.ReactiveEffects?.Values.Any(entry =>
                entry.Effects.Any(effect => effect is FlammableReaction)) != true)
        {
            return;
        }

        // Kritters: flammability, rather than alcohol metadata, determines Novakin Core heating.
        var heatPerUnit = (DangerousHeat - entity.Comp.FuelConsumptionBaselineTemperature)
            / Math.Max(entity.Comp.FlammableUnitsToDangerousHeat, 1f);
        entity.Comp.PendingReagentHeat += args.Quantity.Float() * heatPerUnit;
        entity.Comp.FlammableRegulationSuppressionRemaining = Math.Max(
            entity.Comp.FlammableRegulationSuppressionRemaining,
            entity.Comp.FlammableRegulationSuppressionSeconds);
    }

    private void ApplyPendingReagentHeat(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature, float elapsed)
    {
        if (physiology.PendingReagentHeat <= 0f)
            return;

        var applied = Math.Min(physiology.PendingReagentHeat, physiology.ReagentHeatTransferPerSecond * elapsed);
        physiology.PendingReagentHeat -= applied;
        _temperature.ForceChangeTemperature(uid, temperature.CurrentTemperature + applied, temperature);
    }

    private void UpdateHeatIntoxication(EntityUid uid, NovakinPhysiologyComponent physiology,
        TemperatureComponent temperature)
    {
        if (!_mobState.IsAlive(uid) || temperature.CurrentTemperature < physiology.IntoxicationStartTemperature)
        {
            _statusEffects.TryRemoveStatusEffect(uid, SharedDrunkSystem.DrunkKey);
            _statusEffects.TryRemoveStatusEffect(uid, SlurredSpeechKey);
            return;
        }

        var range = physiology.PeakIntoxicationTemperature - physiology.IntoxicationStartTemperature;
        var progress = range > 0f
            ? Math.Clamp((temperature.CurrentTemperature - physiology.IntoxicationStartTemperature) / range, 0f, 1f)
            : 1f;
        var seconds = MathHelper.Lerp(physiology.MinimumIntoxicationSeconds,
            physiology.PeakIntoxicationSeconds, progress);
        var duration = TimeSpan.FromSeconds(seconds);

        // Kritters: Novakin drunkenness is a live view of Core heat, never accumulated reagent alcohol.
        _statusEffects.TryAddStatusEffect<DrunkComponent>(uid, SharedDrunkSystem.DrunkKey, duration, true);
        _statusEffects.TryAddStatusEffect<SlurredAccentComponent>(uid, SlurredSpeechKey, duration, true);
        _statusEffects.TrySetTime(uid, SharedDrunkSystem.DrunkKey, duration);
        _statusEffects.TrySetTime(uid, SlurredSpeechKey, duration);
    }

    private static void OnBeforeDamageChanged(Entity<NovakinPhysiologyComponent> entity,
        ref BeforeDamageChangedEvent args)
    {
        if (!args.Damage.DamageDict.ContainsKey("Poison"))
            return;

        // Kritters: the Core burns out Poison regardless of whether its source bypasses normal resistances.
        var filtered = new DamageSpecifier(args.Damage);
        filtered.DamageDict.Remove("Poison");
        args = args with { Damage = filtered };
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
