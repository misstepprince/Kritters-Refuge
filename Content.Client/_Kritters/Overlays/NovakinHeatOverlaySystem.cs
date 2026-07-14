using Robust.Client.Graphics;

namespace Content.Client._Kritters.Overlays;

public sealed class NovakinHeatOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    private NovakinHeatOverlay? _overlay;

    public override void Initialize()
    {
        _overlay = new NovakinHeatOverlay();
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        if (_overlay != null)
            _overlays.RemoveOverlay(_overlay);
    }
}
