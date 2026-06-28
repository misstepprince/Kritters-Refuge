using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.BloodTypes.Components;

/// <summary>
/// Extra vending stock only inserted while the Kritters blood type system is enabled.
/// </summary>
[RegisterComponent]
public sealed partial class KrittersBloodTypeVendingStockComponent : Component
{
    [DataField(required: true)]
    public Dictionary<EntProtoId, uint> Entries = new();
}
