using Content.Shared._Kritters.BloodTypes.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.BloodTypes.Components;

/// <summary>
/// Overrides automatic blood type inference for mobs that need explicit data-driven compatibility.
/// </summary>
[RegisterComponent]
public sealed partial class KrittersBloodTypeOverrideComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KrittersBloodTypePrototype> BloodType;
}
