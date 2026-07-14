using Content.Shared.Atmos;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Prototypes;

[Prototype("novakinGas")]
public sealed partial class NovakinGasPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; } = default!;

    [DataField(required: true)]
    public Gas Gas { get; private set; }

    [DataField]
    public float MaxReserve { get; private set; } = 100f;
}
