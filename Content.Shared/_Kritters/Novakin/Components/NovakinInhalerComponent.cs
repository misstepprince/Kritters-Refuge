using Content.Shared._Kritters.Novakin.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Novakin.Components;

/// <summary>
/// Marks a native gas tank as a Novakin reserve inhaler and configures the
/// conversion between stored atmospheric gas and physiology reserve.
/// </summary>
[RegisterComponent]
public sealed partial class NovakinInhalerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NovakinGasPrototype> Gas;

    /// <summary>
    /// Allows a starting-kit inhaler to adopt its owner's profile-selected gas.
    /// Ordinary inhalers retain their prototype gas identity.
    /// </summary>
    [DataField]
    public bool AdaptToOwnerGas;

    /// <summary>
    /// Native gas capacity of the inhaler. At 400 reserve per mole, this stores 50 reserve.
    /// </summary>
    [DataField]
    public float MaxMoles = 0.125f;

    /// <summary>
    /// Reserve restored by one mole of matching gas.
    /// </summary>
    [DataField]
    public float ReservePerMole = 400f;

    /// <summary>
    /// Maximum reserve restored by one use.
    /// </summary>
    [DataField]
    public float TransferAmount = 10f;
}
