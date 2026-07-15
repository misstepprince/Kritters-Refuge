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

    /// <summary>Unprotected intact reserve loss: a full reserve lasts ten minutes.</summary>
    [DataField]
    public float ReserveDrainPerSecond = 1f / 6f;

    [DataField]
    public float ShatteredReserveDrainPerSecond = 100f / 30f;

    /// <summary>Nitrogen loss starts causing blood-loss symptoms below half reserve.</summary>
    [DataField]
    public float BloodlossReserveThreshold = 0.5f;

    [DataField]
    public DamageSpecifier BloodlossDamage = new() { DamageDict = { ["Bloodloss"] = 0.5f } };

    [DataField]
    public DamageSpecifier BloodlossHealDamage = new() { DamageDict = { ["Bloodloss"] = -1f } };

    [DataField]
    public float LeakedMolesPerReserve = 0.01f;

    [DataField]
    public float FuelDepletedCoolingPerSecond = 8f;

    [DataField]
    public float FuelConsumptionBaselineTemperature = 373.15f;

    [DataField]
    public float FuelConsumptionMaximumTemperature = 700f;

    [DataField]
    public float MaximumFuelConsumptionMultiplier = 6f;

    /// <summary>Movement-speed multiplier reached at the 700 K core-temperature cap.</summary>
    [DataField]
    public float MaximumHeatSpeedMultiplier = 1.3f;

    [DataField, AutoNetworkedField]
    public float HeatSpeedMultiplier = 1f;

    public float BaseFuelDecayRate = -1f;

    /// <summary>Elapsed unprotected vacuum exposure for the current spacewalk.</summary>
    public float UnsuitedVacuumTime;

    /// <summary>True only when this system added the current pressure-immunity component.</summary>
    public bool NovakinPressureImmunityAdded;

    public float BaseImplicitHeatRegulation = -1f;
    public float BaseSweatHeatRegulation = -1f;
    public float BaseShiveringHeatRegulation = -1f;

    /// <summary>Set only once a dangerous temperature has driven damage to critical.</summary>
    [DataField, AutoNetworkedField]
    public bool ShellShattered;
}
