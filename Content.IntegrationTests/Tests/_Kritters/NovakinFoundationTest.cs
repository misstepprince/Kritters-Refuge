using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._Kritters.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kritters;

[TestFixture]
public sealed class NovakinFoundationTest
{
    [Test]
    public async Task BloodlessCoreAcceptsCryotubeDoses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var body = entities.System<BodySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<BloodstreamComponent>(novakin), Is.False);
                Assert.That(entities.HasComponent<InjectableSolutionComponent>(novakin), Is.False);
            });

            var core = body.GetBodyOrgans(novakin).Single(organ => entities.HasComponent<StomachComponent>(organ.Id)).Id;
            var stomach = entities.GetComponent<StomachComponent>(core);
            entities.EventBus.RaiseLocalEvent(novakin,
                new NovakinCryoPodInjectionEvent(new Solution("Bicaridine", FixedPoint2.New(1))));

            Assert.That(stomach.ReagentDeltas.Select(delta => delta.ReagentQuantity.Reagent.Prototype), Does.Contain("Bicaridine"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpeciesUsesGasReserveWithoutPointLight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);

            Assert.Multiple(() =>
            {
                Assert.That(physiology.CurrentReserve, Is.EqualTo(physiology.MaxReserve));
                Assert.That(entities.HasComponent<AtmosExposedComponent>(novakin), Is.True);
                Assert.That(entities.HasComponent<TemperatureComponent>(novakin), Is.True);
            });

            Assert.That(entities.GetComponent<TagComponent>(novakin).Tags, Does.Contain("DoorBumpOpener"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NitrogenInhalerRestoresReserve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            var novakin = entities.SpawnEntity("MobNovakin", coordinates);
            var inhaler = entities.SpawnEntity("NovakinInhalerNitrogen", coordinates);
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var tank = entities.GetComponent<GasTankComponent>(inhaler);
            var initialMoles = tank.Air.GetMoles(Gas.Nitrogen);

            physiology.CurrentReserve = 0f;
            entities.EventBus.RaiseLocalEvent(inhaler, new UseInHandEvent(novakin));

            Assert.That(physiology.CurrentReserve, Is.EqualTo(10f).Within(0.001f));
            Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.LessThan(initialMoles));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShellWeaknessAppliesToPhysicalDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var damageable = entities.System<DamageableSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var damage = entities.GetComponent<DamageableComponent>(novakin);

            damageable.TryChangeDamage(novakin, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 10));

            Assert.That(damage.TotalDamage, Is.EqualTo(FixedPoint2.New(11.5f)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CriticalTemperatureShattersShell()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var temperature = entities.GetComponent<TemperatureComponent>(novakin);

            temperature.CurrentTemperature = 1000f;
            physiologySystem.Update(30f);

            Assert.That(entities.GetComponent<NovakinPhysiologyComponent>(novakin).ShellShattered, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HotCoreGrantsThirtyPercentMovementSpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 700f;

            physiologySystem.Update(0.5f);

            Assert.That(entities.GetComponent<NovakinPhysiologyComponent>(novakin).HeatSpeedMultiplier,
                Is.EqualTo(1.3f).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PressureSuitDoesNotContainIntactShellLeak()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var inventory = entities.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var unprotected = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var protectedNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.One, map.MapId));
            var suit = entities.SpawnEntity("ClothingOuterHardsuitEVA", new MapCoordinates(Vector2.One, map.MapId));
            Assert.That(inventory.TryEquip(protectedNovakin, suit, "outerClothing", force: true), Is.True);

            physiologySystem.Update(1f);

            var unprotectedReserve = entities.GetComponent<NovakinPhysiologyComponent>(unprotected).CurrentReserve;
            var protectedReserve = entities.GetComponent<NovakinPhysiologyComponent>(protectedNovakin).CurrentReserve;
            Assert.That(unprotectedReserve, Is.LessThan(100f));
            Assert.That(protectedReserve, Is.EqualTo(unprotectedReserve).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }
}
