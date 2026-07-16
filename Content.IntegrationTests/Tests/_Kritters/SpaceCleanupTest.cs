using System.Numerics;
using Content.Server._Kritters.SpaceCleanup;
using Content.Server._Kritters.SpaceCleanup.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Kritters;

[TestFixture]
public sealed class SpaceCleanupTest
{
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
}
