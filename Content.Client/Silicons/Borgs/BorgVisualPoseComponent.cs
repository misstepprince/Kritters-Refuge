using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;

namespace Content.Client.Silicons.Borgs;

[RegisterComponent]
public sealed partial class BorgVisualPoseComponent : Component
{
    public string IdleState = string.Empty;
    public string? MovingState;
    public string? RestState;
    public string? LyingState;
    public string? DeadState;
    public string MindState = string.Empty;
    public string? MindMovingState;
    public string NoMindState = string.Empty;
    public string? NoMindMovingState;
    public string ToggleLightState = string.Empty;
    public string? ToggleLightMovingState;
    public TimeSpan RestDelay;
    public TimeSpan RestWakeupDelay = TimeSpan.FromSeconds(0.15);
    public TimeSpan? WakeupUntil;
    public bool WasMoving;
    public TimeSpan LastMovementTime;
    public string? CurrentState;
    public string? CurrentMindState;
    public string? CurrentToggleLightState;
    public bool HasLastWorldPosition;
    public Vector2 LastWorldPosition;
    public MapId LastWorldMapId;
}