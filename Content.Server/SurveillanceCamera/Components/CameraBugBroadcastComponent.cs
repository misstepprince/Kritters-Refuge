using Robust.Shared.Prototypes;

namespace Content.Server.SurveillanceCamera;

/// <summary>
///     Marker component for the camera bug that enables a toggleable wireless camera broadcast mode.
///     When broadcasting, a covert camera entity is spawned at the bug's position so remote monitors
///     can observe the area. Toggling off deletes the spawned entity.
/// </summary>
[RegisterComponent]
public sealed partial class CameraBugBroadcastComponent : Component
{
    /// <summary> Whether the broadcast mode is currently active. </summary>
    [ViewVariables]
    public bool Broadcasting { get; set; } = false;

    /// <summary> The spawned covert camera entity, if broadcast is active. </summary>
    [ViewVariables]
    public EntityUid? SpawnedCamera { get; set; }

    /// <summary> Prototype to spawn for the broadcast camera. </summary>
    [DataField]
    public EntProtoId CameraPrototype { get; set; } = "CameraBugCamera";
}
