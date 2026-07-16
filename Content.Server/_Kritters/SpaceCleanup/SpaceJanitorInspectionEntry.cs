using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._Kritters.SpaceCleanup;

/// <summary>
/// A live entity inspection result for Bluespace Janitorial Services.
/// </summary>
public readonly record struct SpaceJanitorInspectionEntry(
    EntityUid Entity,
    string PrototypeId,
    EntityUid? Grid,
    MapId MapId,
    Vector2 Position,
    TimeSpan? Remaining,
    string? IneligibilityReason,
    int ScopePrototypeCount);
