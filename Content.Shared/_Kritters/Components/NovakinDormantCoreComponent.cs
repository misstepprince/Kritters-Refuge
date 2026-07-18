namespace Content.Shared._Kritters.Components;

/// <summary>Marks a dead Novakin whose core can be restarted by a rescuer.</summary>
[RegisterComponent]
public sealed partial class NovakinDormantCoreComponent : Component
{
    /// <summary>
    /// Whether the Novakin revival lifecycle added the entity's unrevivable marker.
    /// </summary>
    public bool OwnsUnrevivable;
}
