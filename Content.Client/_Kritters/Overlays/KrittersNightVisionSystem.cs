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

    private void AttemptAddVision(EntityUid uid)
    {
        if (_player.LocalSession?.AttachedEntity != uid) return;

        //if they currently have flash immunity, dont add
        if (_flashImmunity.HasFlashImmunityVisionBlockers(uid)) return;

        //only add if its active
        if (!TryComp<KrittersNightVisionComponent>(uid, out var nightVision) || !nightVision.Active) return;

        // The light effect is local-only and provides the actual illumination;
        // the overlay supplies the blue-tinted directional presentation.
        if (_effect != null) return;

        _overlayMan.AddOverlay(_overlay);
        _effect = SpawnAttachedTo(nightVision.EffectPrototype, Transform(uid).Coordinates);
        _transform.SetParent(_effect.Value, uid);
        _active = true;
    }

    /// <summary>
    /// Attempt to remove the overlay from the local player.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="force">Use if you need to forcefully remove the overlay no matter what. Only should be used with events that ONLY the local player can fire, like attach/detach</param>
    private void AttemptRemoveVision(EntityUid uid, bool force = false)
    {
        //ENSURE this is the local player
        if (_player.LocalSession?.AttachedEntity != uid && !force) return;

        if (!_active)
            return;

        _overlayMan.RemoveOverlay(_overlay);
        Del(_effect);
        _effect = null;
        _active = false;
    }
}
