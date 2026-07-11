using Content.Shared._Kritters.Novakin.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Novakin.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NovakinPhysiologyComponent : Component
{
    public static readonly ProtoId<NovakinGasPrototype> DefaultGas = "NovakinGasNitrogen";

    [DataField, AutoNetworkedField]
    public ProtoId<NovakinGasPrototype> Gas = DefaultGas;

    [DataField, AutoNetworkedField]
    public float MaxReserve = 100f;

    [DataField, AutoNetworkedField]
    public float CurrentReserve = 100f;

    [DataField]
    public float NormalTemperature = 373.15f;

    [DataField]
    public float StabilizationRate = 30f;

    [DataField]
    public string ThermalProtectionSlot = "outerClothing";

    [ViewVariables]
    public float LastTemperature = 373.15f;
}
