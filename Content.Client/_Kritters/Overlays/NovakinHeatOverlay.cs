using System.Numerics;
using Content.Shared._Kritters.Components;
using Content.Shared.Eye;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Kritters.Overlays;

public sealed partial class NovakinHeatOverlay : Robust.Client.Graphics.Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private readonly ShaderInstance _shader;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public NovakinHeatOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("GradientCircleMask").InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var uid = _player.LocalEntity;
        if (uid == null
            || !_entities.TryGetComponent(uid, out EyeComponent? eye)
            || args.Viewport.Eye != eye.Eye
            || !_entities.TryGetComponent(uid, out NovakinPhysiologyComponent? physiology))
            return;

        // TemperatureComponent is server-only. GlowIntensity is synchronized from the same
        // temperature calculation, so derive the normalized heat level from it client-side.
        var range = physiology.FullGlowEnergy - physiology.MinimumGlowEnergy;
        var level = range > 0f
            ? Math.Clamp((physiology.GlowIntensity * physiology.FullGlowEnergy - physiology.MinimumGlowEnergy) / range, 0f, 1f)
            : physiology.GlowIntensity;
        if (level <= 0f)
            return;

        var distance = args.ViewportBounds.Width;
        _shader.SetParameter("time", 0f);
        _shader.SetParameter("color", new Vector3(1f, 0.28f, 0.03f));
        _shader.SetParameter("darknessAlphaOuter", 0.18f * level);
        _shader.SetParameter("outerCircleRadius", 1.65f * distance);
        _shader.SetParameter("outerCircleMaxRadius", 1.85f * distance);
        _shader.SetParameter("innerCircleRadius", 0.55f * distance);
        _shader.SetParameter("innerCircleMaxRadius", 0.57f * distance);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }
}
