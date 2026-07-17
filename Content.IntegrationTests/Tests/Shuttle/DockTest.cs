using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Tests;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Shuttle;

public sealed class DockTest : ContentUnitTest
{
    private static IEnumerable<object[]> TestSource()
    {
        // I-shape for grid1, T-shape for grid2
        yield return new object[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Angle.Zero, Angle.Zero, true };
        yield return new object[] { new Vector2(0.5f, 1.5f), new Vector2(0.5f, 1.5f), Angle.Zero, Angle.Zero, false };
    }

    [Test]
    [TestCaseSource(nameof(TestSource))]
    public async Task TestDockingConfig(Vector2 dock1Pos, Vector2 dock2Pos, Angle dock1Angle, Angle dock2Angle, bool result)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var dockingSystem = entManager.System<DockingSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var mapId = map.MapId;

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);
            var grid1 = mapManager.CreateGridEntity(mapId);
            var grid2 = mapManager.CreateGridEntity(mapId);
            var grid1Ent = grid1.Owner;
            var grid2Ent = grid2.Owner;
            var grid2Offset = new Vector2(50f, 50f);
            xformSystem.SetLocalPosition(grid2Ent, grid2Offset);

            // Tetris tests
            // Grid1 is a vertical I
            // Grid2 is a T

            var tiles1 = new List<(Vector2i Index, Tile Tile)>()
            {
                new(new Vector2i(0, 0), new Tile(1)),
                new(new Vector2i(0, 1), new Tile(1)),
                new(new Vector2i(0, 2), new Tile(1)),
            };

            mapSystem.SetTiles(grid1.Owner, grid1.Comp, tiles1);
            var dock1 = entManager.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid1Ent, dock1Pos));
            var dock1Xform = entManager.GetComponent<TransformComponent>(dock1);
            dock1Xform.LocalRotation = dock1Angle;

            var tiles2 = new List<(Vector2i Index, Tile Tile)>()
            {
                new(new Vector2i(0, 0), new Tile(1)),
                new(new Vector2i(0, 1), new Tile(1)),
                new(new Vector2i(0, 2), new Tile(1)),
                new(new Vector2i(-1, 2), new Tile(1)),
                new(new Vector2i(1, 2), new Tile(1)),
            };

            mapSystem.SetTiles(grid2.Owner, grid2.Comp, tiles2);
            var dock2 = entManager.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid2Ent, dock2Pos));
            var dock2Xform = entManager.GetComponent<TransformComponent>(dock2);
            dock2Xform.LocalRotation = dock2Angle;

            var config = dockingSystem.GetDockingConfig(grid1Ent, grid2Ent);

            Assert.That(result, Is.EqualTo(config != null));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestPlanetDock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var dockingSystem = entManager.System<DockingSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        MapGridComponent mapGrid = default!;

        var shuttle = EntityUid.Invalid;

        // Spawn shuttle and affirm no valid docks.
        await server.WaitAssertion(() =>
        {
            mapGrid = entManager.AddComponent<MapGridComponent>(map.MapUid);
            entManager.DeleteEntity(map.Grid);
            var path = new ResPath("/Maps/Shuttles/emergency.yml");
            Assert.That(entManager.System<MapLoaderSystem>().TryLoadGrid(otherMap.MapId, path, out var grid));
            shuttle = grid!.Value.Owner;

            var dockingConfig = dockingSystem.GetDockingConfig(shuttle, map.MapUid);
            Assert.That(dockingConfig, Is.EqualTo(null));
        });

        // Spawn dock and affirm it docks with no blockers / doesn't dock with blockers
        await server.WaitAssertion(() =>
        {
            mapSystem.SetTile(map.MapUid, mapGrid, Vector2i.Zero, new Tile(1));
            var airlockEnt = entManager.SpawnEntity("AirlockShuttle", new EntityCoordinates(map.MapUid, Vector2.One / 2f));
            Assert.That(entManager.GetComponent<TransformComponent>(airlockEnt).Anchored);

            var dockingConfig = dockingSystem.GetDockingConfig(shuttle, map.MapUid);
            Assert.That(dockingConfig, Is.Not.EqualTo(null));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ManualDockingConvergesFromAllowedAngularOffset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var docking = entities.System<DockingSystem>();
        var maps = entities.System<SharedMapSystem>();
        var transforms = entities.System<SharedTransformSystem>();
        EntityUid dockA = default;
        EntityUid dockB = default;

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var gridA = maps.CreateGridEntity(map.MapId);
            var gridB = maps.CreateGridEntity(map.MapId);
            maps.SetTile(gridA, Vector2i.Zero, new Tile(1));
            maps.SetTile(gridB, Vector2i.Zero, new Tile(1));

            var gridBRotation = Angle.FromDegrees(10);
            transforms.SetLocalRotation(gridB, gridBRotation);
            var dockLocalPosition = new Vector2(0.5f, 0.5f);
            var dockAWorldPosition = dockLocalPosition;
            var dockBWorldPosition = dockAWorldPosition + Angle.Zero.ToWorldVec() * 1.1f;
            transforms.SetLocalPosition(gridB, dockBWorldPosition - gridBRotation.RotateVec(dockLocalPosition));

            dockA = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(gridA, dockLocalPosition));
            dockB = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(gridB, dockLocalPosition));
            transforms.SetLocalRotation(dockA, Angle.Zero);
            transforms.SetLocalRotation(dockB, new Angle(Math.PI));

            var dockAComponent = entities.GetComponent<DockingComponent>(dockA);
            var dockBComponent = entities.GetComponent<DockingComponent>(dockB);
            Assert.That(docking.CanDock((dockA, dockAComponent), (dockB, dockBComponent)), Is.True);
            docking.Dock((dockA, dockAComponent), (dockB, dockBComponent));
            Assert.That(dockAComponent.DockJoint, Is.Not.Null);
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            var distance = Vector2.Distance(transforms.GetWorldPosition(dockA), transforms.GetWorldPosition(dockB));
            Assert.That(distance, Is.EqualTo(1f).Within(0.02f));
        });

        await pair.CleanReturnAsync();
    }
}
