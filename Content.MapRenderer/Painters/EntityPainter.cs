using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static Robust.UnitTesting.RobustIntegrationTest;

namespace Content.MapRenderer.Painters;

public sealed class EntityPainter
{
    private readonly CpuRsiLoader _rsiLoader;

    private readonly IEntityManager _sEntityManager;
    private readonly SpriteSystem _sprite;

    public EntityPainter(
        ClientIntegrationInstance client,
        ServerIntegrationInstance server,
        CpuRsiLoader rsiLoader)
    {
        _rsiLoader = rsiLoader;
        _sEntityManager = server.ResolveDependency<IEntityManager>();
        _sprite = client.ResolveDependency<IEntityManager>().System<SpriteSystem>();
    }

    public void Preload(IEnumerable<List<EntityData>> entityLists)
    {
        _rsiLoader.Load(CpuRsiLoader.ErrorPath);

        foreach (var entities in entityLists)
        foreach (var entity in entities)
        foreach (var layer in entity.Sprite.AllLayers)
        {
            if (layer.ActualRsi is { } rsi)
                _rsiLoader.Load(rsi.Path);
        }
    }

    public void Run(Image canvas, List<EntityData> entities, Vector2 customOffset = default)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // TODO cache this shit what are we insane
        entities.Sort(Comparer<EntityData>.Create((x, y) => x.Sprite.DrawDepth.CompareTo(y.Sprite.DrawDepth)));
        var xformSystem = _sEntityManager.System<SharedTransformSystem>();

        foreach (var entity in entities)
        {
            Run(canvas, entity, xformSystem, customOffset);
        }

        Console.WriteLine($"{nameof(EntityPainter)} painted {entities.Count} entities in {(int)stopwatch.Elapsed.TotalMilliseconds} ms");
    }

    public void Run(Image canvas, EntityData entity, SharedTransformSystem xformSystem, Vector2 customOffset = default)
    {
        if (!entity.Sprite.Visible || entity.Sprite.ContainerOccluded)
        {
            return;
        }

        var worldRotation = xformSystem.GetWorldRotation(entity.Owner);
        foreach (var layer in entity.Sprite.AllLayers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            if (!layer.RsiState.IsValid)
            {
                continue;
            }

            var rsi = layer.ActualRsi;
            var dir = _sprite.LayerGetDirectionCount((SpriteComponent.Layer)layer) switch
            {
                0 => 0,
                _ => (int)layer.EffectiveDirection(worldRotation)
            };

            Image<Rgba32>? clonedImage = null;
            if (rsi == null ||
                !rsi.TryGetState(layer.RsiState, out var state) ||
                !_rsiLoader.TryGetFrame(rsi.Path, state.StateId, dir, layer.AnimationFrame, out clonedImage))
            {
                _rsiLoader.TryGetFrame(CpuRsiLoader.ErrorPath, "error", 0, 0, out clonedImage);
            }

            if (clonedImage == null)
            {
                Console.WriteLine($"Unable to load layer {rsi?.Path}/{layer.RsiState.Name} for entity {_sEntityManager.ToPrettyString(entity.Owner)} at ({entity.X}, {entity.Y})");
                continue;
            }

            using (clonedImage)
            {
                var spriteRotation = 0f;
                if (!entity.Sprite.NoRotation && !entity.Sprite.SnapCardinals && _sprite.LayerGetDirectionCount((SpriteComponent.Layer)layer) == 1)
                {
                    spriteRotation = (float)worldRotation.Degrees;
                }

                var colorMix = entity.Sprite.Color * layer.Color;
                var imageColor = Color.FromRgba(colorMix.RByte, colorMix.GByte, colorMix.BByte, colorMix.AByte);
                using var coloredImage = new Image<Rgba32>(clonedImage.Width, clonedImage.Height);
                coloredImage.Mutate(o => o.BackgroundColor(imageColor));

                var (imgX, imgY) = rsi?.Size ?? (EyeManager.PixelsPerMeter, EyeManager.PixelsPerMeter);
                var offsetX = (int)(entity.Sprite.Offset.X + customOffset.X) * EyeManager.PixelsPerMeter;
                var offsetY = (int)(entity.Sprite.Offset.Y + customOffset.Y) * EyeManager.PixelsPerMeter;
                clonedImage.Mutate(o => o
                    .DrawImage(coloredImage, PixelColorBlendingMode.Multiply, PixelAlphaCompositionMode.SrcAtop, 1)
                    .Resize(imgX, imgY)
                    .Flip(FlipMode.Vertical)
                    .Rotate(spriteRotation));

                var pointX = (int)entity.X + offsetX - imgX / 2;
                var pointY = (int)entity.Y + offsetY - imgY / 2;
                canvas.Mutate(o => o.DrawImage(clonedImage, new Point(pointX, pointY), 1));
            }
        }
    }
}
