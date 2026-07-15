using Content.Shared._Kritters.Components;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Kritters.Visuals;

/// <summary>Novakin eyes are always bright; the rest of the body is normally lit.</summary>
public sealed partial class NovakinGlowVisualizerSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    private readonly HashSet<EntityUid> _dirty = new();
    private const string EyeGlowKey = "novakin-eyes-glow";

    public override void Initialize()
    {
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentStartup>((Entity<NovakinPhysiologyComponent> e, ref ComponentStartup _) => _dirty.Add(e));
        SubscribeLocalEvent<NovakinPhysiologyComponent, ComponentShutdown>(OnShutdown);
    }

    public override void FrameUpdate(float frameTime)
    {
        foreach (var uid in _dirty)
            UpdateEyes(uid);
        _dirty.Clear();
    }

    private void OnShutdown(Entity<NovakinPhysiologyComponent> entity, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(entity, out var sprite) && sprite.LayerMapTryGet(EyeGlowKey, out var index))
        {
            _sprites.LayerMapRemove((entity, sprite), EyeGlowKey);
            _sprites.RemoveLayer((entity, sprite), index);
        }
    }

    private void UpdateEyes(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite)
            || !sprite.LayerMapTryGet(HumanoidVisualLayers.Eyes, out var eyeIndex)) return;
        var eye = sprite[eyeIndex];
        if (eye.ActualRsi?.Path is not { } path || eye.RsiState.Name is not { } state) return;
        if (!sprite.LayerMapTryGet(EyeGlowKey, out var glowIndex))
        {
            glowIndex = _sprites.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(path, state), eyeIndex + 1);
            _sprites.LayerMapSet((uid, sprite), EyeGlowKey, glowIndex);
        }
        if (!sprite.LayerMapTryGet(HumanoidVisualLayers.Eyes, out eyeIndex)
            || !sprite.LayerMapTryGet(EyeGlowKey, out glowIndex)) return;
        var source = sprite[eyeIndex];
        sprite.LayerSetShader(glowIndex, "unshaded");
        _sprites.LayerSetColor((uid, sprite), glowIndex, source.Color);
        _sprites.LayerSetVisible((uid, sprite), glowIndex, source.Visible);
    }
}
