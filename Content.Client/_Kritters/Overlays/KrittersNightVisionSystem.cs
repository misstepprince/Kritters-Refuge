using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared._Kritters.Overlays;

namespace Content.Client._Kritters.Overlays;

public sealed partial class KrittersNightVisionSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private FlashImmunitySystem _flashImmunity = default!;
    [Dependency] private PointLightSystem _lights = default!;

    private KrittersNightVisionOverlay _overlay = default!;
    [ViewVariables]
    private bool _active;
    private EntityUid? _effect;
    private const string ModernKrittersNightVisionShaderPrototype = "ModernKrittersNightVisionShader";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KrittersNightVisionComponent, ComponentInit>(OnVisionInit);
        SubscribeLocalEvent<KrittersNightVisionComponent, ComponentShutdown>(OnVisionShutdown);

        SubscribeLocalEvent<KrittersNightVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<KrittersNightVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<KrittersNightVisionComponent, FlashImmunityCheckEvent>(OnFlashImmunityChanged);

        _overlay = new(_prototypeManager.Index<ShaderPrototype>(ModernKrittersNightVisionShaderPrototype));
    }

    private void OnFlashImmunityChanged(Entity<KrittersNightVisionComponent> ent, ref FlashImmunityCheckEvent args)
    {
        if (args.IsImmune)
        {
            AttemptRemoveVision(ent.Owner);
        }
        else
        {
            AttemptAddVision(ent.Owner);
        }
    }

    private void OnPlayerAttached(Entity<KrittersNightVisionComponent> ent, ref LocalPlayerAttachedEvent args)
        => AttemptAddVision(ent.Owner);

    private void OnPlayerDetached(Entity<KrittersNightVisionComponent> ent, ref LocalPlayerDetachedEvent args)
        => AttemptRemoveVision(ent.Owner, true);

    private void OnVisionInit(Entity<KrittersNightVisionComponent> ent, ref ComponentInit args)
        => AttemptAddVision(ent.Owner);

    private void OnVisionShutdown(Entity<KrittersNightVisionComponent> ent, ref ComponentShutdown args)
        => AttemptRemoveVision(ent.Owner);

    public override void FrameUpdate(float frameTime)
    {
        if (_player.LocalSession?.AttachedEntity is not { } uid
            || !TryComp<KrittersNightVisionComponent>(uid, out var vision))
            return;

        if (CanDisplayVision(uid, vision))
        {
            AttemptAddVision(uid);
            UpdateVisualState(vision);
        }
        else
        {
            AttemptRemoveVision(uid);
        }
    }

    private void AttemptAddVision(EntityUid uid)
    {
        if (_player.LocalSession?.AttachedEntity != uid
            || !TryComp<KrittersNightVisionComponent>(uid, out var nightVision)
            || !CanDisplayVision(uid, nightVision)
            || _effect != null)
            return;

        _overlayMan.AddOverlay(_overlay);
        _effect = SpawnAttachedTo(nightVision.EffectPrototype, Transform(uid).Coordinates);
        _transform.SetParent(_effect.Value, uid);
        _active = true;
        UpdateVisualState(nightVision);
    }

    private bool CanDisplayVision(EntityUid uid, KrittersNightVisionComponent vision)
        => vision.Active && vision.Illumination > 0.001f && !_flashImmunity.HasFlashImmunityVisionBlockers(uid);

    private void UpdateVisualState(KrittersNightVisionComponent vision)
    {
        _overlay.SetVisualState(vision.Illumination, vision.HeatSaturation, vision.HeatWashout);
        if (_effect is not { } effect || !TryComp<PointLightComponent>(effect, out var light))
            return;

        _lights.SetRadius(effect, MathHelper.Lerp(5f, 20f, vision.Illumination), light);
        _lights.SetEnergy(effect, MathHelper.Lerp(0.15f, 0.8f, vision.Illumination), light);
    }

    /// <summary>
    /// Attempt to remove the overlay from the local player.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="force">Use if you need to forcefully remove the overlay no matter what. Only should be used with events that ONLY the local player can fire, like attach/detach</param>
    private void AttemptRemoveVision(EntityUid uid, bool force = false)
    {
        if (_player.LocalSession?.AttachedEntity != uid && !force) return;

        if (!_active)
            return;

        _overlayMan.RemoveOverlay(_overlay);
        Del(_effect);
        _effect = null;
        _active = false;
    }
}
