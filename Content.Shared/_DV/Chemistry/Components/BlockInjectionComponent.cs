using Robust.Shared.GameStates;

namespace Content.Shared._DV.Chemistry.Components;

/// <summary>
/// Prevents injections being used on this entity.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BlockInjectionComponent : Component
{
    /// <summary>
    /// If true, this component will block injections from syringes.
    /// </summary>
    [DataField]
    public bool BlockSyringe = true;

    /// <summary>
    /// If true, this component will block injections from hypospray.
    /// </summary>
    [DataField]
    public bool BlockHypospray = false; //Coyote: set to false by default

    /// <summary>
    /// If true, this component will block injections from projectile.
    /// </summary>
    [DataField]
    public bool BlockInjectOnProjectile;
}
