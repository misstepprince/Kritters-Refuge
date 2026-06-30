using Content.Shared._Kritters.BloodTypes.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.BloodTypes.Components;

/// <summary>
/// Stores the resolved profile blood type so CVar refreshes preserve player-selected variants.
/// </summary>
[RegisterComponent]
public sealed partial class KrittersBloodTypeSourceComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KrittersBloodTypePrototype> BloodType;
}
