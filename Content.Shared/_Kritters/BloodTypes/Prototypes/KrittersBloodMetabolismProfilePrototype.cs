using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.BloodTypes.Prototypes;

[Prototype("krittersBloodMetabolismProfile")]
public sealed partial class KrittersBloodMetabolismProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Blood type tag that enables this metabolism profile.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<TagPrototype> RequiredTag { get; private set; }

    /// <summary>
    /// Target body temperature used by ThermalRegulatorComponent.
    /// </summary>
    [DataField(required: true)]
    public float NormalBodyTemperature { get; private set; }

    /// <summary>
    /// Whether the entity's current temperature should be moved to the profile temperature when applied.
    /// </summary>
    [DataField]
    public bool SetCurrentTemperature = true;

    /// <summary>
    /// Whether TemperatureSpeed thresholds should be shifted by the same delta as normal body temperature.
    /// </summary>
    [DataField]
    public bool ShiftTemperatureSpeedThresholds = true;

    /// <summary>
    /// Explicit TemperatureSpeed thresholds to apply instead of shifting the entity's existing thresholds.
    /// </summary>
    [DataField]
    public Dictionary<float, float>? TemperatureSpeedThresholds;
}
