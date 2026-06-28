using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.BloodTypes.Components;

/// <summary>
/// Enforces a reagent whitelist for ordinary solution transfers.
/// </summary>
[RegisterComponent]
public sealed partial class KrittersBloodOnlyContainerComponent : Component
{
    [DataField]
    public string Solution = "default";

    [DataField(required: true)]
    public List<ProtoId<ReagentPrototype>> ReagentWhitelist = new();
}
