using Content.Shared._Kritters.Components;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Kritters.Visuals;

/// <summary>
/// Adds unshaded copies of Novakin anatomical layers so their selected skin hue
/// remains luminous in darkness. Layers are inserted directly above each body
/// layer, allowing clothing and equipment to continue covering them normally.
/// </summary>
public sealed partial class NovakinGlowVisualizerSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;

    private readonly HashSet<EntityUid> _dirty = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _markingGlowLayers = new();

    private static readonly HumanoidVisualLayers[] BodyLayers =
    {
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RHand,
        HumanoidVisualLayers.LHand,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RFoot,
        HumanoidVisualLayers.LFoot,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentStartup>(OnGlowChanged);
        SubscribeLocalEvent<NovakinPhysiologyComponent, AfterAutoHandleStateEvent>(OnGlowChanged);
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentShutdown>(OnGlowRemoved);
    }

    public override void FrameUpdate(float frameTime)
    {
        foreach (var uid in _dirty)
            UpdateGlowLayers(uid);

        _dirty.Clear();
    }

    private void OnGlowChanged(Entity<NovakinPhysiologyComponent> entity, ref ComponentStartup args)
        => _dirty.Add(entity);

    private void OnGlowChanged(Entity<NovakinPhysiologyComponent> entity, ref AfterAutoHandleStateEvent args)
        => _dirty.Add(entity);

    private void OnGlowRemoved(Entity<NovakinPhysiologyComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(entity, out var sprite))
            return;

        foreach (var bodyLayer in BodyLayers)
            RemoveGlowLayer(entity, sprite, GlowKey(bodyLayer));

        RemoveGlowLayer(entity, sprite, EyeGlowKey);
        RemoveMarkingGlowLayers(entity, sprite);
    }

    private void UpdateGlowLayers(EntityUid uid)
    {
        if (!TryComp<NovakinPhysiologyComponent>(uid, out var physiology)
            || !TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
            || !TryComp<SpriteComponent>(uid, out var sprite))
        {
            return;
        }

        var glowAlpha = Math.Clamp(
            physiology.GlowIntensity * physiology.MaximumBodyGlowOpacity,
            0f,
            1f);

        foreach (var bodyLayer in BodyLayers)
        {
            var key = GlowKey(bodyLayer);
            if (!sprite.LayerMapTryGet(bodyLayer, out var bodyIndex)
                || !humanoid.BaseLayers.TryGetValue(bodyLayer, out var bodyPrototype)
                || bodyPrototype.BaseSprite == null)
            {
                RemoveGlowLayer(uid, sprite, key);
                continue;
            }

            if (!sprite.LayerMapTryGet(key, out var glowIndex))
            {
                glowIndex = _sprites.AddLayer((uid, sprite), bodyPrototype.BaseSprite, bodyIndex + 1);
                _sprites.LayerMapSet((uid, sprite), key, glowIndex);
            }
            else
            {
                _sprites.LayerSetSprite((uid, sprite), glowIndex, bodyPrototype.BaseSprite);
            }

            var body = sprite[bodyIndex];
            sprite.LayerSetShader(glowIndex, "unshaded");
            _sprites.LayerSetColor((uid, sprite), glowIndex, humanoid.SkinColor.WithAlpha(glowAlpha));
            _sprites.LayerSetVisible((uid, sprite), glowIndex, body.Visible && glowAlpha > 0f);
        }

        UpdateEyeGlowLayer(uid, sprite, glowAlpha);
        UpdateMarkingGlowLayers(uid, sprite, humanoid, glowAlpha);
    }

    /// <summary>
    /// Eyes are not anatomical body layers, so mirror their native humanoid layer separately.
    /// This preserves the character's selected eye color while giving it the same unshaded,
    /// temperature-dependent glow treatment as the body and markings.
    /// </summary>
    private void UpdateEyeGlowLayer(EntityUid uid, SpriteComponent sprite, float glowAlpha)
    {
        if (!sprite.LayerMapTryGet(HumanoidVisualLayers.Eyes, out var eyeIndex))
        {
            RemoveGlowLayer(uid, sprite, EyeGlowKey);
            return;
        }

        var eye = sprite[eyeIndex];
        if (eye.ActualRsi?.Path is not { } rsiPath || eye.RsiState.Name is not { } rsiState)
        {
            RemoveGlowLayer(uid, sprite, EyeGlowKey);
            return;
        }

        if (!sprite.LayerMapTryGet(EyeGlowKey, out var glowIndex))
        {
            var specifier = new SpriteSpecifier.Rsi(rsiPath, rsiState);
            glowIndex = _sprites.AddLayer((uid, sprite), specifier, eyeIndex + 1);
            _sprites.LayerMapSet((uid, sprite), EyeGlowKey, glowIndex);
        }

        // Adding a layer can shift the source index, so resolve it again before copying its presentation.
        if (!sprite.LayerMapTryGet(HumanoidVisualLayers.Eyes, out eyeIndex)
            || !sprite.LayerMapTryGet(EyeGlowKey, out glowIndex))
        {
            return;
        }

        if (!_sprites.TryGetLayer((uid, sprite), eyeIndex, out var eyeLayer, false))
            return;

        sprite.LayerSetShader(glowIndex, "unshaded");
        _sprites.LayerSetColor((uid, sprite), glowIndex,
            eyeLayer.Color.WithAlpha(eyeLayer.Color.A * glowAlpha));
        _sprites.LayerSetScale((uid, sprite), glowIndex, eyeLayer.Scale);
        _sprites.LayerSetOffset((uid, sprite), glowIndex, eyeLayer.Offset);
        _sprites.LayerSetRotation((uid, sprite), glowIndex, eyeLayer.Rotation);
        _sprites.LayerSetVisible((uid, sprite), glowIndex, eyeLayer.Visible && glowAlpha > 0f);
    }

    /// <summary>
    /// Mirrors the marking layers built by the native humanoid appearance system.
    /// This preserves each marking's chosen color and transform while giving it
    /// the same temperature-dependent luminosity as the Novakin body beneath it.
    /// </summary>
    private void UpdateMarkingGlowLayers(
        EntityUid uid,
        SpriteComponent sprite,
        HumanoidAppearanceComponent humanoid,
        float glowAlpha)
    {
        var active = new HashSet<string>();

        foreach (var markingKey in humanoid.ClientElderMarkings)
        {
            if (!sprite.LayerMapTryGet(markingKey, out var markingIndex))
                continue;

            var marking = sprite[markingIndex];
            if (marking.ActualRsi?.Path is not { } rsiPath || marking.RsiState.Name is not { } rsiState)
                continue;

            var glowKey = MarkingGlowKey(markingKey);
            active.Add(glowKey);

            if (!sprite.LayerMapTryGet(glowKey, out var glowIndex))
            {
                var specifier = new SpriteSpecifier.Rsi(rsiPath, rsiState);
                glowIndex = _sprites.AddLayer((uid, sprite), specifier, markingIndex + 1);
                _sprites.LayerMapSet((uid, sprite), glowKey, glowIndex);
            }

            // Adding a layer can shift the source index, so resolve it again before copying its presentation.
            if (!sprite.LayerMapTryGet(markingKey, out markingIndex)
                || !sprite.LayerMapTryGet(glowKey, out glowIndex))
            {
                continue;
            }

            if (!_sprites.TryGetLayer((uid, sprite), markingIndex, out var markingLayer, false))
                continue;

            sprite.LayerSetShader(glowIndex, "unshaded");
            _sprites.LayerSetColor((uid, sprite), glowIndex,
                markingLayer.Color.WithAlpha(markingLayer.Color.A * glowAlpha));
            _sprites.LayerSetScale((uid, sprite), glowIndex, markingLayer.Scale);
            _sprites.LayerSetOffset((uid, sprite), glowIndex, markingLayer.Offset);
            _sprites.LayerSetRotation((uid, sprite), glowIndex, markingLayer.Rotation);
            _sprites.LayerSetVisible((uid, sprite), glowIndex, markingLayer.Visible && glowAlpha > 0f);
        }

        if (_markingGlowLayers.TryGetValue(uid, out var previous))
        {
            foreach (var staleKey in previous)
            {
                if (!active.Contains(staleKey))
                    RemoveGlowLayer(uid, sprite, staleKey);
            }
        }

        _markingGlowLayers[uid] = active;
    }

    private void RemoveMarkingGlowLayers(EntityUid uid, SpriteComponent sprite)
    {
        if (!_markingGlowLayers.Remove(uid, out var keys))
            return;

        foreach (var key in keys)
            RemoveGlowLayer(uid, sprite, key);
    }

    private void RemoveGlowLayer(EntityUid uid, SpriteComponent sprite, string key)
    {
        if (!sprite.LayerMapTryGet(key, out var glowIndex))
            return;

        _sprites.LayerMapRemove((uid, (SpriteComponent?) sprite), key);
        _sprites.RemoveLayer((uid, (SpriteComponent?) sprite), glowIndex);
    }

    private static string GlowKey(HumanoidVisualLayers layer) => $"NovakinGlow-{layer}";

    private const string EyeGlowKey = "NovakinGlow-Eyes";

    private static string MarkingGlowKey(string markingKey) => $"NovakinMarkingGlow-{markingKey}";
}
