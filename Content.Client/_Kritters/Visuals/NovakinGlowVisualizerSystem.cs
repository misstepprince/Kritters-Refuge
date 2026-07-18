using Content.Shared._Kritters.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.SSDIndicator;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Kritters.Visuals;

/// <summary>Renders the Novakin's natural body layers as temperature-driven emissive overlays.</summary>
public sealed partial class NovakinGlowVisualizerSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private readonly HashSet<EntityUid> _tracked = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _markingGlowKeys = new();
    private static readonly HumanoidVisualLayers[] GlowLayers =
    [
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.HeadSide,
        HumanoidVisualLayers.HeadTop,
        HumanoidVisualLayers.Snout,
        HumanoidVisualLayers.Eyes,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RHand,
        HumanoidVisualLayers.LHand,
        HumanoidVisualLayers.RLegBehind,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LLegBehind,
        HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RFootBehind,
        HumanoidVisualLayers.RFoot,
        HumanoidVisualLayers.LFootBehind,
        HumanoidVisualLayers.LFoot,
        HumanoidVisualLayers.Tail,
        HumanoidVisualLayers.TailBehind,
        HumanoidVisualLayers.TailExtras,
        HumanoidVisualLayers.NeckFluff,
    ];

    public override void Initialize()
    {
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentStartup>((Entity<NovakinPhysiologyComponent> e, ref ComponentStartup _) => _tracked.Add(e));
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentShutdown>(OnShutdown);
    }

    public override void FrameUpdate(float frameTime)
    {
        foreach (var uid in _tracked)
            UpdateGlow(uid);
    }

    private void OnShutdown(Entity<NovakinPhysiologyComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(entity, out var sprite))
            return;

        foreach (var layer in GlowLayers)
            RemoveGlowLayer(entity, sprite, GetGlowKey(layer));

        if (_markingGlowKeys.Remove(entity, out var markingKeys))
            foreach (var key in markingKeys)
                RemoveGlowLayer(entity, sprite, key);

        _tracked.Remove(entity);
    }

    private void UpdateGlow(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var intensity = TryComp<NovakinPhysiologyComponent>(uid, out var physiology)
            ? physiology.GlowIntensity
            : 0f;
        foreach (var layer in GlowLayers)
        {
            UpdateGlowLayer(uid, sprite, layer, GetGlowKey(layer), intensity, layer == HumanoidVisualLayers.Eyes);
        }

        UpdateEyesForSsd(uid, sprite);

        UpdateMarkingGlow(uid, sprite, intensity);
    }

    private void UpdateEyesForSsd(EntityUid uid, SpriteComponent sprite)
    {
        var eyesEnabled = !TryComp<SSDIndicatorComponent>(uid, out var ssd) || !ssd.IsSSD;
        if (sprite.LayerMapTryGet(GetGlowKey(HumanoidVisualLayers.Eyes), out var glowIndex))
            _sprites.LayerSetVisible((uid, sprite), glowIndex, eyesEnabled);
    }

    private void UpdateMarkingGlow(EntityUid uid, SpriteComponent sprite, float intensity)
    {
        var currentKeys = new HashSet<string>();
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            foreach (var markings in humanoid.MarkingSet.Markings.Values)
            foreach (var marking in markings)
            {
                if (!_prototypes.TryIndex(marking.MarkingId, out MarkingPrototype? prototype)
                    || prototype.BodyPart is not (HumanoidVisualLayers.Hair
                        or HumanoidVisualLayers.HeadTop
                        or HumanoidVisualLayers.Tail))
                    continue;

                foreach (var markingSprite in prototype.Sprites)
                {
                    if (markingSprite is not SpriteSpecifier.Rsi rsi)
                        continue;

                    var sourceKey = $"{prototype.ID}-{rsi.RsiState}";
                    var glowKey = $"novakin-glow-{sourceKey}";
                    if (!sprite.LayerMapTryGet(sourceKey, out _))
                        continue;

                    currentKeys.Add(glowKey);
                    UpdateGlowLayer(uid, sprite, sourceKey, glowKey, intensity, false);
                }
            }
        }

        if (_markingGlowKeys.TryGetValue(uid, out var oldKeys))
            foreach (var key in oldKeys.Where(key => !currentKeys.Contains(key)))
                RemoveGlowLayer(uid, sprite, key);
        _markingGlowKeys[uid] = currentKeys;
    }

    private void UpdateGlowLayer(EntityUid uid, SpriteComponent sprite, object sourceKey, string glowKey, float intensity, bool eyes)
    {
        if (!sprite.LayerMapTryGet(sourceKey, out var sourceIndex))
            return;

        var source = sprite[sourceIndex];
        if (source.ActualRsi?.Path is not { } path || source.RsiState.Name is not { } state)
            return;

        if (!sprite.LayerMapTryGet(glowKey, out var glowIndex))
        {
            glowIndex = _sprites.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(path, state), sourceIndex + 1);
            _sprites.LayerMapSet((uid, sprite), glowKey, glowIndex);
        }

        var alpha = eyes ? MathHelper.Lerp(0.45f, 1f, intensity) : MathHelper.Lerp(0.05f, 0.9f, intensity);
        sprite.LayerSetShader(glowIndex, "unshaded");
        _sprites.LayerSetColor((uid, sprite), glowIndex, source.Color.WithAlpha(alpha));
        _sprites.LayerSetVisible((uid, sprite), glowIndex, source.Visible);
    }

    private void RemoveGlowLayer(EntityUid uid, SpriteComponent sprite, string key)
    {
        if (!sprite.LayerMapTryGet(key, out var index))
            return;

        _sprites.LayerMapRemove((uid, sprite), key);
        _sprites.RemoveLayer((uid, sprite), index);
    }

    private static string GetGlowKey(HumanoidVisualLayers layer) => $"novakin-glow-{layer}";
}
