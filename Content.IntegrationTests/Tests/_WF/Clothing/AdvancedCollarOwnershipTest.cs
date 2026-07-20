using Content.Shared.Clothing.Components;
using Content.Shared.Construction.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Clothing;

[TestFixture]
public sealed class AdvancedCollarOwnershipTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: AdvancedCollarOwnershipTestCollar
  components:
  - type: AdvancedCollar

- type: entity
  id: AdvancedCollarOwnershipTestPreExistingCollar
  components:
  - type: AdvancedCollar
  - type: Anchorable

- type: entity
  id: AdvancedCollarOwnershipTestModule
  components:
  - type: AdvancedCollarModule
    componentToAdd: Anchorable
    componentsToAdd:
    - Anchorable
";

    [Test]
    public async Task PreExistingComponentSurvivesModuleRemoval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var containers = server.System<SharedContainerSystem>();
            var collar = entities.SpawnEntity("AdvancedCollarOwnershipTestPreExistingCollar", map.GridCoords);
            var module = entities.SpawnEntity("AdvancedCollarOwnershipTestModule", map.GridCoords);
            var collarComponent = entities.GetComponent<AdvancedCollarComponent>(collar);

            Assert.That(containers.Insert(module, collarComponent.ModuleContainer), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<AdvancedCollarModuleComponent>(module).InstalledIn, Is.EqualTo(collar));
                Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.True);
                Assert.That(collarComponent.ModuleOwnedComponents, Is.Empty);
            });

            Assert.That(containers.RemoveEntity(collar, module), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<AdvancedCollarModuleComponent>(module).InstalledIn, Is.Null);
                Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.True);
                Assert.That(collarComponent.ModuleOwnedComponents, Is.Empty);
            });
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DuplicateRequestsKeepOwnedComponentUntilFinalRemoval(bool removeSecondFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var containers = server.System<SharedContainerSystem>();
            var collar = entities.SpawnEntity("AdvancedCollarOwnershipTestCollar", map.GridCoords);
            var firstModule = entities.SpawnEntity("AdvancedCollarOwnershipTestModule", map.GridCoords);
            var secondModule = entities.SpawnEntity("AdvancedCollarOwnershipTestModule", map.GridCoords);
            var collarComponent = entities.GetComponent<AdvancedCollarComponent>(collar);

            Assert.That(containers.Insert(firstModule, collarComponent.ModuleContainer), Is.True);
            Assert.That(containers.Insert(secondModule, collarComponent.ModuleContainer), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.True);
                Assert.That(collarComponent.ModuleOwnedComponents, Is.EquivalentTo(new[] { "Anchorable" }));
            });

            var firstRemoved = removeSecondFirst ? secondModule : firstModule;
            var lastRemoved = removeSecondFirst ? firstModule : secondModule;
            Assert.That(containers.RemoveEntity(collar, firstRemoved), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<AdvancedCollarModuleComponent>(firstRemoved).InstalledIn, Is.Null);
                Assert.That(entities.GetComponent<AdvancedCollarModuleComponent>(lastRemoved).InstalledIn, Is.EqualTo(collar));
                Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.True);
                Assert.That(collarComponent.ModuleOwnedComponents, Is.EquivalentTo(new[] { "Anchorable" }));
            });

            Assert.That(containers.RemoveEntity(collar, lastRemoved), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<AdvancedCollarModuleComponent>(lastRemoved).InstalledIn, Is.Null);
                Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.False);
                Assert.That(collarComponent.ModuleOwnedComponents, Is.Empty);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingCollarComponentCleansOnlyOwnedEffects()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var containers = server.System<SharedContainerSystem>();
            var collar = entities.SpawnEntity("AdvancedCollarOwnershipTestCollar", map.GridCoords);
            var module = entities.SpawnEntity("AdvancedCollarOwnershipTestModule", map.GridCoords);
            var collarComponent = entities.GetComponent<AdvancedCollarComponent>(collar);

            Assert.That(containers.Insert(module, collarComponent.ModuleContainer), Is.True);
            Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.True);

            entities.RemoveComponent<AdvancedCollarComponent>(collar);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<AdvancedCollarModuleComponent>(module).InstalledIn, Is.Null);
                Assert.That(entities.HasComponent<AnchorableComponent>(collar), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }
}
