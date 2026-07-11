using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Novakin.Overlay;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KrittersNightVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active = true;

    [DataField]
    public EntProtoId EffectPrototype = "EffectKrittersNightVision";

    public bool Clothes;
}

[RegisterComponent]
public sealed partial class ClothesKrittersNightVisionComponent : Component
{ }