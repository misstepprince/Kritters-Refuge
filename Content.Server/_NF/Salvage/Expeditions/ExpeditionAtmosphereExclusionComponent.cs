using Robust.Shared.Map;

namespace Content.Server._NF.Salvage.Expeditions;

/// <summary>
/// Tracks landing zones on an expedition grid where map atmosphere should not be applied.
/// Prevents atmospheric gases from affecting landed ships and their immediate surroundings.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionAtmosphereExclusionComponent : Component
{
    /// <summary>
    /// Landing zones (in world coordinates) that should be excluded from map atmosphere application.
    /// </summary>
    [ViewVariables]
    public List<Box2> ExcludedZones { get; set; } = new();
}
