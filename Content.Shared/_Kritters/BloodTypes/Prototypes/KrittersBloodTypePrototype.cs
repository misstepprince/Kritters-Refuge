using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.BloodTypes.Prototypes;

[Prototype("krittersBloodType")]
public sealed partial class KrittersBloodTypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name { get; private set; } = default!;

    [DataField]
    public string? Description { get; private set; }

    /// <summary>
    /// Kritters: Short localized label shown by health analyzers.
    /// </summary>
    [DataField]
    public string? ScannerName { get; private set; }

    /// <summary>
    /// Reagent used by BloodstreamComponent when this blood type is explicitly selected.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> BloodReagent { get; private set; }

    /// <summary>
    /// Tags applied to spawned entities for reagent effect compatibility checks.
    /// </summary>
    [DataField]
    public List<ProtoId<TagPrototype>> Tags { get; private set; } = new();

    /// <summary>
    /// Optional species allow-list. Empty means this blood type is available to all round-start species.
    /// </summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> Species { get; private set; } = new();
}
