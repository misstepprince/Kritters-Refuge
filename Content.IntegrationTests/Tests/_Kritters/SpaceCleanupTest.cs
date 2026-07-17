using System.Numerics;
using System.Collections.Generic;
using Content.Server._Kritters.SpaceCleanup;
using Content.Server._Kritters.SpaceCleanup.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Kritters;

[TestFixture]
public sealed class SpaceCleanupTest
{
    [Test]
    public async Task CommandCompletionIncludesValidArguments()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var console = server.ResolveDependency<IConsoleHost>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var command = console.AvailableCommands["spacejanny"];

            var subcommands = command.GetCompletion(console.LocalShell, [""]);
            Assert.That(subcommands.Options.Select(option => option.Value), Does.Contain("cleanup-cull"));
            Assert.That(subcommands.Options.Select(option => option.Value), Does.Contain("grid-cull"));

            var prototypes = command.GetCompletion(console.LocalShell, ["cleanup-cull", "Crow"]);
            Assert.That(prototypes.Options.Select(option => option.Value), Does.Contain("Crowbar"));

            var components = command.GetCompletion(console.LocalShell, ["list", "has:MobS"]);
            Assert.That(components.Options.Select(option => option.Value), Does.Contain("has:MobState"));

            var gridId = entities.GetNetEntity(map.Grid.Owner).ToString();
            var grids = command.GetCompletion(console.LocalShell, ["grid", ""]);
            Assert.That(grids.Options.Select(option => option.Value), Does.Contain(gridId));

            var gridCullPrototype = command.GetCompletion(console.LocalShell, ["grid-cull", "Crow"]);
            Assert.That(gridCullPrototype.Options.Select(option => option.Value), Does.Contain("Crowbar"));

            var gridCullGrid = command.GetCompletion(console.LocalShell, ["grid-cull", gridId]);
            Assert.That(gridCullGrid.Options.Select(option => option.Value), Does.Contain(gridId));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovesExpiredLooseItemsButHonorsExemptions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid disposable = default;
        EntityUid exempt = default;

        await server.WaitAssertion(() =>
        {
            disposable = entities.SpawnEntity("Crowbar", new MapCoordinates(new Vector2(100, 100), map.MapId));
            exempt = entities.SpawnEntity("Crowbar", new MapCoordinates(new Vector2(101, 100), map.MapId));
            entities.AddComponent<AggressiveSpaceJanitorExemptComponent>(exempt);

            janitor.RunCleanup();
            var tracker = entities.GetComponent<AggressiveSpaceJanitorTrackedComponent>(disposable);
            tracker.Remaining = TimeSpan.Zero;
            tracker.LastAccountedAt = TimeSpan.Zero;

            janitor.RunCleanup();
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorTrackedComponent>(exempt), Is.False);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(entities.EntityExists(disposable), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForceGridCleanupRemovesItemsButNotMobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid item = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            item = entities.SpawnEntity("Crowbar", map.GridCoords);
            mob = entities.SpawnEntity("MobNovakin", new EntityCoordinates(map.Grid.Owner, 1, 0));
            janitor.RunGridCleanup(map.Grid.Owner);
            Assert.That(entities.GetComponent<AggressiveSpaceJanitorTrackedComponent>(item).Remaining,
                Is.EqualTo(TimeSpan.FromMinutes(5)));
            janitor.RunCleanup();
            Assert.That(entities.GetComponent<AggressiveSpaceJanitorTrackedComponent>(item).TargetGrid,
                Is.EqualTo(map.Grid.Owner));
            Assert.That(janitor.CancelGridCleanup(map.Grid.Owner), Is.EqualTo(1));
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorTrackedComponent>(item), Is.False);
            janitor.RunGridCleanup(map.Grid.Owner);
            Assert.That(janitor.ForceGridCleanup(map.Grid.Owner), Is.EqualTo(1));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(item), Is.False);
            Assert.That(entities.EntityExists(mob), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForceSpaceCleanupCountsAndRemovesOnlyLooseItems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid item = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            item = entities.SpawnEntity("Crowbar", new MapCoordinates(new Vector2(100, 100), map.MapId));
            mob = entities.SpawnEntity("MobNovakin", new MapCoordinates(new Vector2(101, 100), map.MapId));

            Assert.That(janitor.GetForceEligibleCount(), Is.EqualTo(1));
            Assert.That(janitor.ForceSpaceCleanup(), Is.EqualTo(1));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(item), Is.False);
            Assert.That(entities.EntityExists(mob), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpaceCleanupCanBeCancelled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var item = entities.SpawnEntity("Crowbar", new MapCoordinates(new Vector2(100, 100), map.MapId));
            janitor.RunSpaceCleanup();
            Assert.That(entities.GetComponent<AggressiveSpaceJanitorTrackedComponent>(item).Remaining,
                Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(janitor.CancelSpaceCleanup(), Is.GreaterThanOrEqualTo(1));
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorTrackedComponent>(item), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridPrototypeCleanupOnlyRemovesMatchingEntitiesOnTargetGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        EntityUid matching = default;
        EntityUid nonMatching = default;
        EntityUid otherGridMatch = default;

        await server.WaitAssertion(() =>
        {
            matching = entities.SpawnEntity("Crowbar", map.GridCoords);
            nonMatching = entities.SpawnEntity("Wrench", new EntityCoordinates(map.Grid.Owner, 1, 0));
            otherGridMatch = entities.SpawnEntity("Crowbar", otherMap.GridCoords);

            Assert.That(janitor.ForceGridPrototypeCleanup(map.Grid.Owner, "Crowbar"), Is.EqualTo(1));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(matching), Is.False);
            Assert.That(entities.EntityExists(nonMatching), Is.True);
            Assert.That(entities.EntityExists(otherGridMatch), Is.True);
        });
        await server.WaitPost(() => entities.DeleteEntity(map.MapUid));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridPrototypeCleanupRemovesUnmindedPests()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid mouse = default;

        await server.WaitAssertion(() =>
        {
            mouse = entities.SpawnEntity("MobMouse", map.GridCoords);
            Assert.That(janitor.ForceGridPrototypeCleanup(map.Grid.Owner, "MobMouse"), Is.EqualTo(1));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(entities.EntityExists(mouse), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpacePrototypeCleanupOnlyRemovesMatchingLooseEntities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid matching = default;
        EntityUid nonMatching = default;
        EntityUid gridMatch = default;

        await server.WaitAssertion(() =>
        {
            matching = entities.SpawnEntity("Crowbar", new MapCoordinates(new Vector2(100, 100), map.MapId));
            nonMatching = entities.SpawnEntity("Wrench", new MapCoordinates(new Vector2(101, 100), map.MapId));
            gridMatch = entities.SpawnEntity("Crowbar", map.GridCoords);

            Assert.That(janitor.ForceSpacePrototypeCleanup("Crowbar"), Is.EqualTo(1));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(matching), Is.False);
            Assert.That(entities.EntityExists(nonMatching), Is.True);
            Assert.That(entities.EntityExists(gridMatch), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PrototypeCleanupSupportsMultiplePrototypesAndProtectsAnchoredOrExemptEntities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid crowbar = default;
        EntityUid crowbarRed = default;
        EntityUid anchored = default;
        EntityUid exempt = default;

        await server.WaitAssertion(() =>
        {
            crowbar = entities.SpawnEntity("Crowbar", map.GridCoords);
            crowbarRed = entities.SpawnEntity("CrowbarRed", map.GridCoords);
            anchored = entities.SpawnEntity("Crowbar", new EntityCoordinates(map.Grid.Owner, 2, 0));
            exempt = entities.SpawnEntity("CrowbarRed", map.GridCoords);
            entities.System<SharedTransformSystem>().AnchorEntity(anchored, entities.GetComponent<TransformComponent>(anchored));
            entities.AddComponent<AggressiveSpaceJanitorExemptComponent>(exempt);

            var prototypeIds = new HashSet<string>(StringComparer.Ordinal) { "Crowbar", "CrowbarRed" };
            Assert.That(janitor.ForceGridPrototypeCleanup(map.Grid.Owner, prototypeIds), Is.EqualTo(2));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(crowbar), Is.False);
            Assert.That(entities.EntityExists(crowbarRed), Is.False);
            Assert.That(entities.EntityExists(anchored), Is.True);
            Assert.That(entities.EntityExists(exempt), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridPrototypeExemptionsAreScopedAndCanBeSwept()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        EntityUid crowbar = default;
        EntityUid crowbarRed = default;
        EntityUid otherGridCrowbar = default;

        await server.WaitAssertion(() =>
        {
            crowbar = entities.SpawnEntity("Crowbar", map.GridCoords);
            crowbarRed = entities.SpawnEntity("CrowbarRed", map.GridCoords);
            otherGridCrowbar = entities.SpawnEntity("Crowbar", otherMap.GridCoords);
            var prototypeIds = new HashSet<string>(StringComparer.Ordinal) { "Crowbar", "CrowbarRed" };
            Assert.That(janitor.SetGridPrototypeExemption(map.Grid.Owner, prototypeIds, exempt: true), Is.EqualTo(2));
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorExemptComponent>(crowbar), Is.True);
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorExemptComponent>(crowbarRed), Is.True);
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorTrackedComponent>(crowbar), Is.False);
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorExemptComponent>(otherGridCrowbar), Is.False);

            Assert.That(janitor.SetGridPrototypeExemption(map.Grid.Owner, prototypeIds, exempt: false), Is.EqualTo(2));
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorExemptComponent>(crowbar), Is.False);
            Assert.That(entities.HasComponent<AggressiveSpaceJanitorExemptComponent>(crowbarRed), Is.False);
        });

        await server.WaitPost(() => entities.DeleteEntity(map.MapUid));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InspectionListsAllEntitiesWithFiltersAndCleanupState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var janitor = entities.System<AggressiveSpaceJanitorSystem>();
        var map = await pair.CreateTestMap();
        EntityUid spaceCrowbar = default;
        EntityUid gridCrowbar = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            spaceCrowbar = entities.SpawnEntity("Crowbar", new MapCoordinates(new Vector2(100, 100), map.MapId));
            gridCrowbar = entities.SpawnEntity("Crowbar", map.GridCoords);
            mob = entities.SpawnEntity("MobNovakin", new EntityCoordinates(map.Grid.Owner, 1, 0));
            janitor.RunCleanup();

            var crowbars = janitor.GetInspectionEntries(new[] { "crow" }, Array.Empty<Type>());
            Assert.That(crowbars.Count(entry => entry.PrototypeId == "Crowbar"), Is.EqualTo(2));
            var spaceEntry = crowbars.Single(entry => entry.Entity == spaceCrowbar);
            Assert.That(spaceEntry.Grid, Is.Null);
            Assert.That(spaceEntry.Remaining, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(spaceEntry.ScopePrototypeCount, Is.EqualTo(1));

            var gridEntry = crowbars.Single(entry => entry.Entity == gridCrowbar);
            Assert.That(gridEntry.Grid, Is.EqualTo(map.Grid.Owner));
            Assert.That(gridEntry.Remaining, Is.Null);
            Assert.That(gridEntry.IneligibilityReason, Is.EqualTo("on a grid"));
            Assert.That(gridEntry.ScopePrototypeCount, Is.EqualTo(1));

            var mobs = janitor.GetInspectionEntries(Array.Empty<string>(), new[] { typeof(MobStateComponent) });
            var mobEntry = mobs.Single(entry => entry.Entity == mob);
            Assert.That(mobEntry.Remaining, Is.Null);
            Assert.That(mobEntry.IneligibilityReason, Is.EqualTo("mob"));
        });

        await pair.CleanReturnAsync();
    }
}
