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

    [DataField]
    public float FuelDepletedCoolingPerSecond = 8f;

    /// <summary>
    /// Core temperature at or below which Fuel uses its normal needs-system decay rate.
    /// </summary>
    [DataField]
    public float FuelConsumptionBaselineTemperature = 373.15f;

    /// <summary>
    /// Core temperature at which increased Fuel consumption reaches its maximum.
    /// </summary>
    [DataField]
    public float FuelConsumptionMaximumTemperature = 700f;

    /// <summary>
    /// Fuel-decay multiplier at <see cref="FuelConsumptionMaximumTemperature"/>.
    /// </summary>
    [DataField]
    public float MaximumFuelConsumptionMultiplier = 2f;

    [DataField]
    public float MaximumHeatSpeedMultiplier = 1.25f;

    [DataField, AutoNetworkedField]
    public float HeatSpeedMultiplier = 1f;

    /// <summary>
    /// The unmodified decay rate supplied by the Fuel need prototype.
    /// </summary>
    public float BaseFuelDecayRate = -1f;

    /// <summary>
    /// Reserve consumed per second while the Novakin is alive or critical and outside cryostorage.
    /// </summary>
    [DataField]
    public float ReserveDrainPerSecond = 1f / 18f;

    /// <summary>
    /// Drain multiplier at full reserve, representing pressure-driven leakage.
    /// </summary>
    [DataField]
    public float FullReserveDrainMultiplier = 1f;

    /// <summary>
    /// Pressure-driven drain multiplier at an empty reserve, before critical instability is applied.
    /// </summary>
    [DataField]
    public float EmptyReserveDrainMultiplier = 0.25f;

    /// <summary>
    /// Reserve fraction below which a collapsing containment field accelerates leakage.
    /// </summary>
    [DataField]
    public float CriticalReserveThreshold = 0.25f;

    /// <summary>
    /// Drain multiplier reached at an empty reserve while containment is critically unstable.
    /// </summary>
    [DataField]
    public float CriticalReserveDrainMultiplier = 4f;

    /// <summary>
    /// Shape of the critical instability curve. Higher values defer rapid loss until reserve is nearly empty.
    /// </summary>
    [DataField]
    public float CriticalReserveDrainExponent = 2f;

    /// <summary>
    /// Additional reserve-drain multiplier while the Novakin is in medical critical condition.
    /// </summary>
    [DataField]
    public float CriticalHealthDrainMultiplier = 4f;

    /// <summary>
    /// Moles of the selected gas released into the local atmosphere per reserve lost.
    /// </summary>
    [DataField]
    public float LeakedMolesPerReserve = 0.01f;

    /// <summary>
    /// Leak multiplier while a pressure-protective outer suit is worn.
    /// </summary>
    [DataField]
    public float PressureSuitLeakMultiplier = 0.25f;

    /// <summary>
    /// Fraction of native thermal regulation retained at an empty reserve.
    /// </summary>
    [DataField]
    public float EmptyReserveThermalRegulationMultiplier = 0.25f;

    /// <summary>
    /// Thermal-regulation multiplier at the critical reserve threshold, before containment collapses.
    /// </summary>
    [DataField]
    public float CriticalReserveThermalRegulationMultiplier = 0.6f;

    /// <summary>
    /// Shape of the thermal containment collapse below the critical reserve threshold.
    /// </summary>
    [DataField]
    public float CriticalReserveThermalRegulationExponent = 2f;

    /// <summary>
    /// Reserve fraction below which structural damage begins accumulating.
    /// </summary>
    [DataField]
    public float DamageThreshold = 0.25f;

    /// <summary>
    /// Cellular damage dealt per second at an empty reserve.
    /// </summary>
    [DataField]
    public float EmptyReserveDamagePerSecond = 1f;

    /// <summary>
    /// HUD alert used to display the remaining gas reserve as a 0-10 gauge.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> ReserveAlert = "NovakinGasReserve";

    [DataField]
    public float MinimumGlowTemperature = 330f;

    [DataField]
    public float FullGlowEnergy = 3f;

    [DataField]
    public float MinimumGlowEnergy = 0.15f;

    [DataField]
    public float DeadGlowEnergy = 0.1f;

    /// <summary>
    /// Maximum opacity of the client-side unshaded body layers.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaximumBodyGlowOpacity = 0.85f;

    /// <summary>
    /// Normalized body luminosity calculated from temperature and life state.
    /// </summary>
    [AutoNetworkedField]
    public float GlowIntensity = 1f;

    [ViewVariables]
    public float LastTemperature = 373.15f;

    [ViewVariables]
    public float BaseImplicitHeatRegulation = -1f;

    [ViewVariables]
    public float BaseSweatHeatRegulation = -1f;

    [ViewVariables]
    public float BaseShiveringHeatRegulation = -1f;
}
