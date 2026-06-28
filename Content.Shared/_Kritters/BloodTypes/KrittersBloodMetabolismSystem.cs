using Content.Shared.Temperature.Components;
using System.Linq;

namespace Content.Shared._Kritters.BloodTypes;

public sealed class KrittersBloodMetabolismSystem : EntitySystem
{
    public void SetTemperatureSpeedThresholds(
        EntityUid uid,
        TemperatureSpeedComponent temperatureSpeed,
        Dictionary<float, float> thresholds)
    {
        temperatureSpeed.Thresholds = new Dictionary<float, float>(thresholds);
        ResetTemperatureSpeed(uid, temperatureSpeed);
    }

    public void ShiftTemperatureSpeedThresholds(
        EntityUid uid,
        TemperatureSpeedComponent temperatureSpeed,
        float temperatureDelta)
    {
        if (MathF.Abs(temperatureDelta) < 0.001f)
            return;

        temperatureSpeed.Thresholds = temperatureSpeed.Thresholds
            .ToDictionary(entry => entry.Key + temperatureDelta, entry => entry.Value);
        ResetTemperatureSpeed(uid, temperatureSpeed);
    }

    private void ResetTemperatureSpeed(EntityUid uid, TemperatureSpeedComponent temperatureSpeed)
    {
        temperatureSpeed.CurrentSpeedModifier = null;
        temperatureSpeed.NextSlowdownUpdate = null;
        Dirty(uid, temperatureSpeed);
    }
}
