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
    public bool? ScanMode;
    public bool? Bleeding;
    public bool? Unrevivable;
    public bool? Unclonable; // Frontier
    public bool Printable; // Frontier
    public string? BloodTypeName; // Kritters
    public Color BloodTypeColor; // Kritters
    public bool HasBloodTypeColor; // Kritters
    // Kritters: null for every non-Novakin target.
    public float? NovakinIntegrity;
    // Kritters: localized gas name for Novakin targets.
    public string? NovakinGasName;

    public HealthAnalyzerUiState() {}

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode, bool? bleeding, bool? unrevivable, bool? unclonable, bool printable = false, string? bloodTypeName = null, Color bloodTypeColor = default, bool hasBloodTypeColor = false, float? novakinIntegrity = null, string? novakinGasName = null) // Frontier: added unclonable, printable // Kritters: blood type display and optional Novakin diagnostics
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
        Unclonable = unclonable; // Frontier
        Printable = printable; // Frontier
        BloodTypeName = bloodTypeName; // Kritters
        BloodTypeColor = hasBloodTypeColor ? bloodTypeColor : Color.White; // Kritters
        HasBloodTypeColor = hasBloodTypeColor; // Kritters
        NovakinIntegrity = novakinIntegrity; // Kritters
        NovakinGasName = novakinGasName; // Kritters
    }
}
