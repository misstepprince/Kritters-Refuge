using Content.Shared.Temperature.Components;

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

        var shiftedThresholds = new Dictionary<float, float>(temperatureSpeed.Thresholds.Count);
        foreach (var (threshold, modifier) in temperatureSpeed.Thresholds)
        {
            shiftedThresholds[threshold + temperatureDelta] = modifier;
        }

        temperatureSpeed.Thresholds = shiftedThresholds;
        ResetTemperatureSpeed(uid, temperatureSpeed);
    }

    private void ResetTemperatureSpeed(EntityUid uid, TemperatureSpeedComponent temperatureSpeed)
    {
        temperatureSpeed.CurrentSpeedModifier = null;
        temperatureSpeed.NextSlowdownUpdate = null;
        Dirty(uid, temperatureSpeed);
    }
}
