using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Overlays;

/// <summary>
/// Enables the Novakin night-vision overlay and identifies the attached visual
/// effect spawned while that overlay is active.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KrittersNightVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active = true;

    [DataField]
    public EntProtoId EffectPrototype = "EffectKrittersNightVision";

    /// <summary>
    /// True when night vision was granted by equipped clothing rather than being
    /// an innate component of the wearer.
    /// </summary>
    public bool Clothes;
}

/// <summary>
/// Internal notification used to suppress special vision while flash-immunity
/// equipment that blocks vision effects is active.
/// </summary>
public sealed class FlashImmunityCheckEvent : EntityEventArgs
{
    public readonly bool IsImmune;

    public FlashImmunityCheckEvent(bool isImmune)
    {
        IsImmune = isImmune;
    }
}
