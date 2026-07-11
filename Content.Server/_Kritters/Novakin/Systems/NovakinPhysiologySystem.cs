using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Kritters.Novakin.Components;
using Content.Shared._Kritters.Novakin.Systems;
using Content.Shared.Inventory;

namespace Content.Server._Kritters.Novakin.Systems;

public sealed class NovakinPhysiologySystem : SharedNovakinPhysiologySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < 0.5f)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<NovakinPhysiologyComponent, TemperatureComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var physiology, out var temperature, out var transform))
        {
            if (IsThermallyProtected(uid, physiology))
            {
                var change = Math.Clamp(
                    physiology.NormalTemperature - temperature.CurrentTemperature,
                    -physiology.StabilizationRate * elapsed,
                    physiology.StabilizationRate * elapsed);
                _temperature.ForceChangeTemperature(uid, temperature.CurrentTemperature + change, temperature);
            }
            else if (temperature.CurrentTemperature < physiology.LastTemperature)
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

    private bool IsThermallyProtected(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        return _inventory.TryGetSlotEntity(uid, physiology.ThermalProtectionSlot, out var outerwear)
            && HasComp<PressureProtectionComponent>(outerwear);
    }
}
