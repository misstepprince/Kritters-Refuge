namespace Content.Shared._Kritters.Components;

/// <summary>
/// Marks a native gas tank as a Novakin reserve inhaler and configures the
/// conversion between stored atmospheric gas and physiology reserve.
/// </summary>
[RegisterComponent]
public sealed partial class NovakinInhalerComponent : Component
{
    /// <summary>
    /// Native gas capacity of the inhaler, matching its normal filled tank equivalent.
    /// </summary>
    [DataField]
    public float MaxMoles = 0.270782035f;

    /// <summary>
    /// Reserve restored by one mole of matching gas.
    /// </summary>
    [DataField]
    public float ReservePerMole = 400f;

}
