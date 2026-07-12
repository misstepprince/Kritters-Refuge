using Content.Shared._Kritters.Novakin.Prototypes;
using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Novakin.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class NovakinPhysiologyComponent : Component
{
    public static readonly ProtoId<NovakinGasPrototype> DefaultGas = "NovakinGasNitrogen";

    [DataField, AutoNetworkedField]
    public ProtoId<NovakinGasPrototype> Gas = DefaultGas;

    [DataField, AutoNetworkedField]
    public float MaxReserve = 100f;

    [DataField, AutoNetworkedField]
    public float CurrentReserve = 100f;

    /// <summary>
    /// Reserve consumed per second while the Novakin is alive or critical and outside cryostorage.
    /// </summary>
    [DataField]
    public float ReserveDrainPerSecond = 1f / 18f;

    /// <summary>
    /// HUD alert used to display the remaining gas reserve as a 0-10 gauge.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> ReserveAlert = "NovakinGasReserve";

    [DataField]
    public float FullGlowTemperature = 373.15f;

    [DataField]
    public float MinimumGlowTemperature = 330f;

    [DataField]
    public float FullGlowEnergy = 1f;

    [DataField]
    public float MinimumGlowEnergy = 0.15f;

    [DataField]
    public float DeadGlowEnergy = 0.1f;

    /// <summary>
    /// Maximum opacity of the client-side unshaded body layers.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaximumBodyGlowOpacity = 0.6f;

    /// <summary>
    /// Normalized body luminosity calculated from temperature and life state.
    /// </summary>
    [AutoNetworkedField]
    public float GlowIntensity = 1f;

    [ViewVariables]
    public float LastTemperature = 373.15f;
}
