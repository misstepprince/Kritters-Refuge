using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Overlays;

/// <summary>
/// Enables the Novakin night-vision overlay.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KrittersNightVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active = true;

    [DataField, AutoNetworkedField]
    public float Illumination = 1f;

    [DataField, AutoNetworkedField]
    public float HeatSaturation;

    [DataField, AutoNetworkedField]
    public float HeatWashout;

    [DataField]
    public EntProtoId EffectPrototype = "EffectKrittersNightVision";

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
