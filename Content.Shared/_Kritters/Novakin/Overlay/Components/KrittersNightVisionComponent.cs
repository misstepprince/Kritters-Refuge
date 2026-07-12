using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Novakin.Overlay;

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
/// Grants Novakin-style night vision while this component's clothing is equipped.
/// This is retained as an extension point for future goggles or similar equipment.
/// </summary>
[RegisterComponent]
public sealed partial class ClothesKrittersNightVisionComponent : Component
{
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
