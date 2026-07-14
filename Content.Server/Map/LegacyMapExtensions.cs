using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Robust.Shared.Map;

/// <summary>
/// Compatibility overloads for map helpers whose engine API now accepts an explicit grid component.
/// </summary>
public static class LegacyMapExtensions
{
    public static EntityCoordinates SnapToGrid(this EntityCoordinates coordinates, IEntityManager entityManager, SharedMapSystem mapSystem)
    {
        var gridUid = entityManager.System<SharedTransformSystem>().GetGrid(coordinates);
        return entityManager.TryGetComponent(gridUid, out MapGridComponent? grid)
            ? mapSystem.GridTileToLocal(gridUid.Value, grid, mapSystem.CoordinatesToTile(gridUid.Value, grid, coordinates))
            : coordinates;
    }

    public static TileRef? GetTileRef(this EntityCoordinates coordinates, IEntityManager entityManager, SharedMapSystem mapSystem)
    {
        var gridUid = coordinates.GetGridUid(entityManager);
        return gridUid is { } uid && entityManager.TryGetComponent(uid, out MapGridComponent? grid)
            ? mapSystem.GetTileRef(uid, grid, coordinates)
            : null;
    }

    public static Vector2i ToVector2i(this EntityCoordinates coordinates, IEntityManager entityManager, SharedMapSystem mapSystem, SharedTransformSystem transformSystem)
    {
        var gridUid = coordinates.GetGridUid(entityManager);
        return gridUid is { } uid
            ? mapSystem.TileIndicesFor(uid, entityManager.GetComponent<MapGridComponent>(uid), coordinates)
            : Vector2i.Zero;
    }

}
