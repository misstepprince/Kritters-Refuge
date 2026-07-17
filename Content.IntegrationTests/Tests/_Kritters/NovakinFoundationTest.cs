using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._Kritters.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Overlays;
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
using Content.Shared.DoAfter;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
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
    public async Task SpeciesStartsWithThirtyMinuteNitrogenReserve()
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

            Assert.That(physiology.ReserveDrainPerSecond * 60f * 30f,
                Is.EqualTo(physiology.MaxReserve).Within(0.01f));
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

            Assert.That(damage.TotalDamage, Is.EqualTo(FixedPoint2.New(12f)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StandardTopicalsHealAndRepeatForNovakin()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var damageable = entities.System<DamageableSystem>();
        var doAfter = entities.System<SharedDoAfterSystem>();
        var map = await pair.CreateTestMap();

        EntityUid novakin = default;
        EntityUid ointment = default;
        await server.WaitAssertion(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            novakin = entities.SpawnEntity("MobNovakin", coordinates);
            ointment = entities.SpawnEntity("Ointment", coordinates);
            damageable.TryChangeDamage(novakin, new DamageSpecifier { DamageDict = { ["Heat"] = 10 } });

            Assert.That(doAfter.TryStartDoAfter(new DoAfterArgs(entities, novakin, 0.01f,
                new HealingDoAfterEvent(), novakin, target: novakin, used: ointment)
            {
                NeedHand = false,
                BreakOnMove = false,
            }), Is.True);
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var damage = entities.GetComponent<DamageableComponent>(novakin);
            var stack = entities.GetComponent<StackComponent>(ointment);
            Assert.Multiple(() =>
            {
                Assert.That(damage.Damage.DamageDict["Heat"], Is.EqualTo(FixedPoint2.Zero));
                Assert.That(stack.Count, Is.EqualTo(8));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CriticalTemperatureStressesShellBeforeFailure()
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
            physiologySystem.Update(5f);

            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var damage = entities.GetComponent<DamageableComponent>(novakin);
            Assert.Multiple(() =>
            {
                Assert.That(physiology.ShellShattered, Is.False);
                Assert.That(GetDamage(damage, "Blunt"), Is.GreaterThan(0f));
                Assert.That(GetDamage(damage, "Heat"), Is.EqualTo(0f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CompromisedShellTakesMinimalThermalDamageAtEmptyReserve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var damageable = entities.System<DamageableSystem>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var temperature = entities.GetComponent<TemperatureComponent>(novakin);
            temperature.CurrentTemperature = 1000f;
            entities.GetComponent<NovakinPhysiologyComponent>(novakin).CurrentReserve = 0f;
            damageable.TryChangeDamage(novakin, new DamageSpecifier { DamageDict = { ["Blunt"] = 100f } });

            physiologySystem.Update(0.5f);
            var damage = entities.GetComponent<DamageableComponent>(novakin);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<NovakinPhysiologyComponent>(novakin).ShellShattered, Is.True);
                Assert.That(GetDamage(damage, "Heat"), Is.GreaterThan(0f));
                Assert.That(GetDamage(damage, "Blunt"), Is.GreaterThanOrEqualTo(100f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HotCoreGrantsFifteenPercentMovementSpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 699f;

            physiologySystem.Update(0.5f);

            Assert.That(entities.GetComponent<NovakinPhysiologyComponent>(novakin).HeatSpeedMultiplier,
                Is.EqualTo(1.15f).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PressureSuitHalvesIntactShellLeak()
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
            Assert.That(100f - protectedReserve, Is.EqualTo((100f - unprotectedReserve) * 0.5f).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PressureSuitContainsCompromisedShell()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var inventory = entities.System<InventorySystem>();
        var damageable = entities.System<DamageableSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var unprotected = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var protectedNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.One, map.MapId));
            var suit = entities.SpawnEntity("ClothingOuterHardsuitEVA", new MapCoordinates(Vector2.One, map.MapId));
            Assert.That(inventory.TryEquip(protectedNovakin, suit, "outerClothing", force: true), Is.True);
            damageable.TryChangeDamage(unprotected, new DamageSpecifier { DamageDict = { ["Blunt"] = 100f } });
            damageable.TryChangeDamage(protectedNovakin, new DamageSpecifier { DamageDict = { ["Blunt"] = 100f } });

            physiologySystem.Update(0.5f);

            var unprotectedLost = 100f - entities.GetComponent<NovakinPhysiologyComponent>(unprotected).CurrentReserve;
            var protectedLost = 100f - entities.GetComponent<NovakinPhysiologyComponent>(protectedNovakin).CurrentReserve;
            Assert.Multiple(() =>
            {
                Assert.That(unprotectedLost, Is.EqualTo(100f / 206f).Within(0.001f));
                Assert.That(protectedLost, Is.EqualTo(0.125f).Within(0.001f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LowReserveBloodlossStartsAtTwentyFivePercentWithoutColdDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            physiology.CurrentReserve = 12f;

            physiologySystem.Update(0.5f);

            var damage = entities.GetComponent<DamageableComponent>(novakin);
            Assert.Multiple(() =>
            {
                Assert.That(GetDamage(damage, "Bloodloss"), Is.GreaterThan(0f));
                Assert.That(GetDamage(damage, "Cold"), Is.EqualTo(0f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ColdSpeedAndNightVisionFollowCoreTemperature()
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
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var vision = entities.GetComponent<KrittersNightVisionComponent>(novakin);

            temperature.CurrentTemperature = 323.15f;
            physiologySystem.Update(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(physiology.ColdSpeedMultiplier, Is.EqualTo(0.95f).Within(0.001f));
                Assert.That(vision.Illumination, Is.EqualTo(0.5f).Within(0.001f));
            });

            temperature.CurrentTemperature = 273.15f;
            physiologySystem.Update(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(physiology.ColdSpeedMultiplier, Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(vision.Illumination, Is.EqualTo(0f).Within(0.001f));
            });

            temperature.CurrentTemperature = 650f;
            physiologySystem.Update(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(vision.HeatSaturation, Is.EqualTo(1f).Within(0.001f));
                Assert.That(vision.HeatWashout, Is.EqualTo(0f).Within(0.001f));
            });
        });

        await pair.CleanReturnAsync();
    }

    private static float GetDamage(DamageableComponent damageable, string damageType)
        => damageable.Damage.DamageDict.TryGetValue(damageType, out var damage) ? damage.Float() : 0f;
}
