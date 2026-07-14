using Robust.Shared.GameStates;

namespace Content.Shared.Flash.Components;

// Kritters edit - moved this to Shared for BlocksSpecialVision
/// <summary>
///     Makes the entity immune to being flashed.
///     When given to clothes in the "head", "eyes" or "mask" slot it protects the wearer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState] // Goob and Kritters edit
public sealed partial class FlashImmunityComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("enabled")]
    public bool Enabled { get; set; } = true;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("protectionRange")]
    public float ProtectionRange { get; set; } = 0f;

    /// <summary>
    /// If true, this item blocks special vision while worn.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    [DataField]
    public bool BlocksSpecialVision { get; set; } = true;
}
