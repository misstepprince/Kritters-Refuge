using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Kritters.Novakin.Components;
using Content.Shared._Kritters.Novakin.Systems;
using Content.Shared.Alert;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;

namespace Content.Server._Kritters.Novakin.Systems;

public sealed class NovakinPhysiologySystem : SharedNovakinPhysiologySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
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
            if (!_mobState.IsDead(uid, mobState) && !HasComp<CryostorageContainedComponent>(uid))
                RemoveReserve((uid, physiology), physiology.ReserveDrainPerSecond * elapsed);

            UpdateReserveAlert(uid, physiology);
            UpdateGlow(uid, physiology, temperature, mobState);

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
}
