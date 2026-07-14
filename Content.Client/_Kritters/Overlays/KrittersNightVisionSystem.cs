using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared._Kritters.Overlays;

namespace Content.Client._Kritters.Overlays;

public sealed class KrittersNightVisionSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly TransformSystem _xformSys = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly FlashImmunitySystem _flashImmunity = default!;

    private KrittersNightVisionOverlay _overlay = default!;
    [ViewVariables]
    private EntityUid? _effect = null;
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

        //only add if effect isnt already used
        if (_effect != null) return;

        _overlayMan.AddOverlay(_overlay);

        _effect = SpawnAttachedTo(nightVision.EffectPrototype, Transform(uid).Coordinates);
        _xformSys.SetParent(_effect.Value, uid);
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

        _overlayMan.RemoveOverlay(_overlay);
        Del(_effect);
        _effect = null;
    }
}
