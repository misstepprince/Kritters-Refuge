using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._Kritters.Overlays;

/// <summary>
/// Draws the Novakin night-vision shader over the active player's world viewport.
/// This remains separate from the fork's other vision overlays so the imported
/// Shadekin visual effect can be preserved without changing DogVision or UltraVision.
/// </summary>
public sealed partial class KrittersNightVisionOverlay : Robust.Client.Graphics.Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private readonly ShaderInstance _shader;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KrittersNightVisionOverlay(ShaderPrototype shader)
    {
        IoCManager.InjectDependencies(this);
        _shader = shader.InstanceUnique();

        // Keep this above ordinary world overlays without reserving a shared enum
        // for a single Novakin-specific effect.
        ZIndex = 10001;
    }

    public void SetFacing(float angle) => _shader.SetParameter("FacingAngle", angle);

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player == null || !_entityManager.TryGetComponent(player, out EyeComponent? eye))
            return false;

        // Only draw into the viewport controlled by the attached player's eye.
        return args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
