using Robust.Client.Graphics;

namespace Content.Client._Kritters.Novakin.Overlay;

public sealed class KrittersNightVisionOverlay : BaseVisionOverlay
{
    public KrittersNightVisionOverlay(ShaderPrototype shader) : base(shader)
        => ZIndex = (int?)OverlayZIndexes.KrittersNightVision;
}