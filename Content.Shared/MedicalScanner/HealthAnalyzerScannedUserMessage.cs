using Content.Shared.Mobs;
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// On interacting with an entity retrieves the entity UID for use with getting the current damage of the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState State;

    public HealthAnalyzerScannedUserMessage(HealthAnalyzerUiState state)
    {
        State = state;
    }
}

/// <summary>
/// Contains the current state of a health analyzer control. Used for the health analyzer and cryo pod.
/// </summary>
[Serializable, NetSerializable]
public struct HealthAnalyzerUiState
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    // Kritters: NaN means the scanned entity does not expose Novakin nitrogen physiology.
    public float NitrogenReserve;
    public bool? ScanMode;
    public MobState? MobState;
    public bool? Bleeding;
    public bool? Unrevivable;
    public bool? Unclonable; // Frontier
    public bool Printable; // Frontier
    public string? BloodTypeName; // Kritters
    public Color BloodTypeColor; // Kritters
    public bool HasBloodTypeColor; // Kritters

    public HealthAnalyzerUiState() {}

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, float nitrogenReserve,
        bool? scanMode, MobState? mobState, bool? bleeding, bool? unrevivable, bool? unclonable,
        bool printable = false, string? bloodTypeName = null, Color bloodTypeColor = default,
        bool hasBloodTypeColor = false) // Frontier: added unclonable, printable
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        NitrogenReserve = nitrogenReserve;
        ScanMode = scanMode;
        MobState = mobState;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
        Unclonable = unclonable; // Frontier
        Printable = printable; // Frontier
        BloodTypeName = bloodTypeName; // Kritters
        BloodTypeColor = hasBloodTypeColor ? bloodTypeColor : Color.White; // Kritters
        HasBloodTypeColor = hasBloodTypeColor; // Kritters
    }

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode,
        MobState? mobState, bool? bleeding, bool? unrevivable, bool? unclonable, bool printable = false,
        string? bloodTypeName = null, Color bloodTypeColor = default, bool hasBloodTypeColor = false)
        : this(targetEntity, temperature, bloodLevel, float.NaN, scanMode, mobState, bleeding, unrevivable,
            unclonable, printable, bloodTypeName, bloodTypeColor, hasBloodTypeColor)
    {
    }
}
