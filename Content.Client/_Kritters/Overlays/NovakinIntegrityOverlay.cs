using System.Numerics;
using Content.Shared._Kritters.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using static Robust.Shared.Maths.Color;

namespace Content.Client._Kritters.Overlays;

/// <summary>
/// Kritters: draws medical-HUD structural-integrity bars for Novakin bodies.
/// </summary>
public sealed class NovakinIntegrityOverlay : Robust.Client.Graphics.Overlay
{
    private readonly IEntityManager _entityManager;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public NovakinIntegrityOverlay(IEntityManager entityManager)
    {
        _entityManager = entityManager;
        _transform = entityManager.System<SharedTransformSystem>();
        _sprite = entityManager.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var rotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var rotationMatrix = Matrix3Helpers.CreateRotation(-rotation);
        var transformQuery = _entityManager.GetEntityQuery<TransformComponent>();
        var query = _entityManager.AllEntityQueryEnumerator<NovakinPhysiologyComponent, SpriteComponent>();

        while (query.MoveNext(out var uid, out var physiology, out var sprite))
        {
            if (!transformQuery.TryGetComponent(uid, out var transform) || transform.MapID != args.MapId)
                continue;

            var bounds = _sprite.GetLocalBounds((uid, sprite));
            var worldPosition = _transform.GetWorldPosition(transform, transformQuery);
            if (!bounds.Translated(worldPosition).Intersects(args.WorldAABB))
                continue;

            var ratio = physiology.MaxReserve > 0f
                ? Math.Clamp(physiology.CurrentReserve / physiology.MaxReserve, 0f, 1f)
                : 0f;
            handle.SetTransform(Matrix3x2.Multiply(rotationMatrix, Matrix3Helpers.CreateTranslation(worldPosition)));

            var width = bounds.Width * EyeManager.PixelsPerMeter;
            const float startX = 8f;
            var endX = width - 8f;
            if (endX <= startX)
                continue;

            // The extra vertical spacing keeps this cyan integrity bar distinct from the damage bar.
            var yOffset = bounds.Height * EyeManager.PixelsPerMeter / 2 + 2f;
            var position = new Vector2(-width / EyeManager.PixelsPerMeter / 2, yOffset / EyeManager.PixelsPerMeter);
            var background = new Box2(new Vector2(startX, 0f) / EyeManager.PixelsPerMeter, new Vector2(endX, 3f) / EyeManager.PixelsPerMeter).Translated(position);
            var progress = new Box2(new Vector2(startX, 0f) / EyeManager.PixelsPerMeter, new Vector2(startX + (endX - startX) * ratio, 3f) / EyeManager.PixelsPerMeter).Translated(position);
            handle.DrawRect(background, Black.WithAlpha(192));
            handle.DrawRect(progress, Cyan);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
