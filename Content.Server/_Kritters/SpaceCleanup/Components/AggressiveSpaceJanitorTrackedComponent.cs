namespace Content.Server._Kritters.SpaceCleanup.Components;

/// <summary>
/// Server-side state for an entity being considered by the aggressive space janitor.
/// </summary>
[RegisterComponent]
public sealed partial class AggressiveSpaceJanitorTrackedComponent : Component
{
    public TimeSpan Remaining;
    public TimeSpan LastAccountedAt;
    public bool Started;
    public EntityUid? TargetGrid;
    public bool Expedited;
}
