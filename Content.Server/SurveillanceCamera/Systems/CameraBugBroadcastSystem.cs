using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Content.Shared.Popups;

namespace Content.Server.SurveillanceCamera;

/// <summary>
///     Handles the camera bug's wireless broadcast toggle.
///     When enabled, a covert camera entity is spawned and parented to the camera bug,
///     allowing it to appear on wireless surveillance camera monitors.
/// </summary>
public sealed class CameraBugBroadcastSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CameraBugBroadcastComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CameraBugBroadcastComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnGetVerbs(EntityUid uid, CameraBugBroadcastComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var text = component.Broadcasting
            ? Loc.GetString("camera-bug-broadcast-disable")
            : Loc.GetString("camera-bug-broadcast-enable");

        AlternativeVerb verb = new()
        {
            Text = text,
            Icon = new Robust.Shared.Utility.SpriteSpecifier.Texture(
                new Robust.Shared.Utility.ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            Act = () => ToggleBroadcast(uid, component, args.User)
        };
        args.Verbs.Add(verb);
    }

    private void ToggleBroadcast(EntityUid uid, CameraBugBroadcastComponent component, EntityUid user)
    {
        if (component.Broadcasting)
        {
            StopBroadcast(uid, component);
            _popup.PopupEntity(Loc.GetString("camera-bug-broadcast-stopped"), uid, user);
        }
        else
        {
            StartBroadcast(uid, component);
            _popup.PopupEntity(Loc.GetString("camera-bug-broadcast-started"), uid, user);
        }
    }

    private void StartBroadcast(EntityUid uid, CameraBugBroadcastComponent component)
    {
        var coords = Transform(uid).Coordinates;
        var camera = Spawn(component.CameraPrototype, coords);
        _transform.SetParent(camera, uid);
        component.SpawnedCamera = camera;
        component.Broadcasting = true;
    }

    private void StopBroadcast(EntityUid uid, CameraBugBroadcastComponent component)
    {
        if (component.SpawnedCamera != null)
        {
            Del(component.SpawnedCamera.Value);
            component.SpawnedCamera = null;
        }
        component.Broadcasting = false;
    }

    private void OnShutdown(EntityUid uid, CameraBugBroadcastComponent component, ComponentShutdown args)
    {
        StopBroadcast(uid, component);
    }
}
