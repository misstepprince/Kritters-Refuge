using Robust.Shared.GameStates;
using Content.Shared.Damage;

namespace Content.Shared._Kritters.Components;

/// <summary>
/// The compact Novakin survival state. Nitrogen is deliberately a scalar reserve,
/// not a solution or a selectable blood type.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class NovakinPhysiologyComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MaxReserve = 100f;

    [DataField, AutoNetworkedField]
    public float CurrentReserve = 100f;

    /// <summary>An intact shell's reserve loss. A full shell lasts thirty minutes.</summary>
    [DataField]
    public float ReserveDrainPerSecond = 100f / (30f * 60f);

    /// <summary>Resource-decay multiplier for an SSD Novakin's dormant Core.</summary>
    [DataField]
    public float SsdReserveDrainMultiplier = 0.1f;

    /// <summary>Fuel-decay multiplier for an SSD Novakin's dormant Core.</summary>
    [DataField]
    public float SsdFuelDecayMultiplier = 0.1f;

    /// <summary>An uncontained compromised shell vents a full reserve in 206 seconds.</summary>
    [DataField]
    public float ShellFailureReserveDrainPerSecond = 100f / 206f;

    [DataField]
    public float PressureSuitReserveDrainMultiplier = 0.5f;

    /// <summary>Pressure suits act as temporary containment for a compromised shell.</summary>
    [DataField]
    public float PressureSuitShellFailureReserveDrainMultiplier = 0.125f / (100f / 206f);

    [DataField]
    public float ShellFailureTemperatureTransferMultiplier = 4f;

    [DataField]
    public float PressureSuitShellFailureTemperatureTransferMultiplier = 0.25f;

    [DataField]
    public float MaximumHeatResourceDrainMultiplier = 1.5f;

    [DataField]
    public float LowReserveMinimumSpeedMultiplier = 0.75f;

    /// <summary>Nitrogen loss starts causing blood-loss symptoms below one quarter reserve.</summary>
    [DataField]
    public float BloodlossReserveThreshold = 0.25f;

    [DataField]
    public DamageSpecifier BloodlossDamage = new() { DamageDict = { ["Bloodloss"] = 0.25f } };

    [DataField]
    public DamageSpecifier BloodlossHealDamage = new() { DamageDict = { ["Bloodloss"] = -1f } };

    [DataField]
    public float LeakedMolesPerReserve = 0.01f;

    [DataField]
    public float FuelDepletedCoolingPerSecond = 4f;

    [DataField]
    public float ThermalStressDamagePerSecond = 0.25f;

    [DataField]
    public float ThermalDamageScaleRange = 50f;

    [DataField]
    public float MaximumThermalStressDamagePerSecond = 0.5f;

    /// <summary>Persistent environmental damage after a shell has failed.</summary>
    [DataField]
    public float CompromisedShellThermalDamagePerSecond = 0.1f;

    /// <summary>Gas-powered thermal damage added while a compromised Core retains reserve.</summary>
    [DataField]
    public float GasThermalDamagePerSecond = 0.9f;

    [DataField]
    public float GasThermalDamageExponent = 1.17f;

    [DataField]
    public float FuelConsumptionBaselineTemperature = 373.15f;

    /// <summary>Movement-speed multiplier reached at 650 K.</summary>
    [DataField]
    public float MaximumHeatSpeedMultiplier = 1.3f;

    [DataField]
    public float MaximumHeatSpeedTemperature = 650f;

    [DataField]
    public float ColdSpeedStartTemperature = 350f;

    [DataField]
    public float ColdSpeedDangerTemperature = 323.15f;

    [DataField]
    public float ColdSpeedMinimumTemperature = 273.15f;

    [DataField]
    public float ColdSpeedAtDangerTemperature = 0.95f;

    [DataField]
    public float MinimumColdSpeedMultiplier = 0.75f;

    [DataField, AutoNetworkedField]
    public float HeatSpeedMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ColdSpeedMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ReserveSpeedMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public bool ThermalWarningHot;

    [DataField, AutoNetworkedField]
    public bool ThermalWarningCold;

    /// <summary>Temperature-derived visual intensity for client sprite glow.</summary>
    [DataField, AutoNetworkedField]
    public float GlowIntensity = 0.5f;

    public float BaseImplicitHeatRegulation = -1f;
    public float BaseSweatHeatRegulation = -1f;
    public float BaseShiveringHeatRegulation = -1f;
    public float BaseAtmosTemperatureTransferEfficiency = -1f;

    /// <summary>True while collective Brute damage has broken the shell.</summary>
    [DataField, AutoNetworkedField]
    public bool ShellShattered;
}
