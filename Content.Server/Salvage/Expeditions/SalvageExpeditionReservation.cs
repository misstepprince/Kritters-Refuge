using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Salvage.Expeditions;

public static class SalvageExpeditionReservation
{
    public static readonly float MinimumLandingClearanceTiles = 5f;

    public static Box2 GetLandingZone(Box2 shuttleBox, Vector2 origin, float padding = 16f)
    {
        // Keep the raw shuttle footprint; clearance is applied by distance checks.
        // This produces a more natural rounded clearance area than storing a pre-enlarged rectangle.
        return shuttleBox.Translated(origin);
    }

    public static Box2 GetShuttleFootprint(Box2 shuttleBox, Vector2 origin)
    {
        return shuttleBox.Translated(origin);
    }

    public static float DistanceBetweenAreas(Box2 a, Box2 b)
    {
        var dx = MathF.Max(0f, MathF.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = MathF.Max(0f, MathF.Max(a.Bottom - b.Top, b.Bottom - a.Top));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static bool IsWithinClearance(Box2 a, Box2 b, float minimumClearance)
    {
        var distance = DistanceBetweenAreas(a, b);
        return minimumClearance <= 0f ? distance <= 0f : distance < minimumClearance;
    }

    public static bool IntersectsDungeonBounds(SalvageExpeditionComponent expedition, Box2 area, float dungeonPadding = 0f)
    {
        var dungeon = dungeonPadding > 0f
            ? expedition.DungeonBounds.Enlarged(dungeonPadding)
            : expedition.DungeonBounds;

        return dungeon.Intersects(area);
    }

    public static bool IntersectsReservedLandingZone(SalvageExpeditionComponent expedition, Box2 area, float minimumClearance = 0f)
    {
        foreach (var zone in expedition.ReservedLandingZones)
        {
            if (IsWithinClearance(zone, area, minimumClearance))
                return true;
        }

        return false;
    }

    public static bool IsReservedTile(SalvageExpeditionComponent expedition, MapGridComponent grid, Vector2i tile)
    {
        if (expedition.ReservedTiles.Contains(tile))
            return true;

        var min = new Vector2(tile.X, tile.Y);
        var max = min + new Vector2(grid.TileSize, grid.TileSize);
        var tileBox = new Box2(min, max);

        // Compatibility fallback for expeditions created before exact tile reservation existed.
        var reserveByBounds = expedition.ReservedTiles.Count == 0 && IntersectsDungeonBounds(expedition, tileBox);

        return reserveByBounds ||
               IntersectsReservedLandingZone(expedition, tileBox);
    }
}