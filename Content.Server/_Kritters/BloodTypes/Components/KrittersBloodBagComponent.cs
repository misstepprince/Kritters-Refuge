using Content.Shared.FixedPoint;

namespace Content.Server._Kritters.BloodTypes.Components;

[RegisterComponent]
public sealed partial class KrittersBloodBagComponent : Component
{
    [DataField]
    public string Solution = "bag";

    [DataField]
    public FixedPoint2 TransferRate = FixedPoint2.New(5);

    [DataField]
    public float MaxConnectionRange = 2f;

    [DataField]
    public EntityUid? AttachedTarget;

    [DataField]
    public EntityUid? AttachedUser;

    [DataField]
    public float Accumulator;
}
