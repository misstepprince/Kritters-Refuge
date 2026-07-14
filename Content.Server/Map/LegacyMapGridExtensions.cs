using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Robust.Shared.Map.Components;

/// <summary>
/// Compatibility helpers for grid methods moved to <see cref="SharedMapSystem"/>.
/// </summary>
public static class LegacyMapGridExtensions
{
    public static Vector2i TileIndicesFor(this MapGridComponent grid, EntityCoordinates coordinates)
    {
        var mapSystem = IoCManager.Resolve<IEntityManager>().System<SharedMapSystem>();
        return mapSystem.TileIndicesFor(grid.Owner, grid, coordinates);
    }

    public static IEnumerable<EntityUid> GetAnchoredEntities(this MapGridComponent grid, Vector2i coordinates)
    {
        var mapSystem = IoCManager.Resolve<IEntityManager>().System<SharedMapSystem>();
        return mapSystem.GetAnchoredEntities(grid.Owner, grid, coordinates);
    }
}
