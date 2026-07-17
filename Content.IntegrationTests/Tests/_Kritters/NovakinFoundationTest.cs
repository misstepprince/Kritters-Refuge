using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Medical;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._Kritters.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._CS.Needs;
using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Overlays;
using Content.Shared._Kritters.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Interaction.Events;
using Content.Shared.DoAfter;
using Content.Shared.Drunk;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffect;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kritters;

[TestFixture]
public sealed class NovakinFoundationTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: NovakinUnsupportedMetabolismReagent
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  metabolisms:
    Gas:
      effects: []
";

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
    public async Task CondimentsFuelNovakinAccordingToEnergyValue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = entities.System<SharedSolutionContainerSystem>();
        var needs = entities.System<SharedNeedsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid hotNovakin = default;
        EntityUid mildNovakin = default;

        await server.WaitAssertion(() =>
        {
            hotNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            mildNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.One, map.MapId));
            Assert.That(needs.TrySetNeedLevel(hotNovakin, NeedType.Fuel, 0f), Is.True);
            Assert.That(needs.TrySetNeedLevel(mildNovakin, NeedType.Fuel, 0f), Is.True);

            Assert.That(solutions.TryGetSolution(hotNovakin, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var hotSolution, out _), Is.True);
            Assert.That(solutions.TryGetSolution(mildNovakin, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var mildSolution, out _), Is.True);
            Assert.That(solutions.TryAddReagent(hotSolution!.Value, "Hotsauce", FixedPoint2.New(1)), Is.True);
            Assert.That(solutions.TryAddReagent(mildSolution!.Value, "Ketchup", FixedPoint2.New(1)), Is.True);
        });

        await pair.RunTicksSync(40);
        await server.WaitAssertion(() =>
        {
            var hotFuel = needs.TryGetNeedLevel(hotNovakin, NeedType.Fuel);
            var mildFuel = needs.TryGetNeedLevel(mildNovakin, NeedType.Fuel);
            Assert.That(hotFuel, Is.Not.Null);
            Assert.That(mildFuel, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(hotFuel!.Value, Is.GreaterThan(mildFuel!.Value));
                Assert.That(mildFuel.Value, Is.GreaterThan(0f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PositiveFoodAndDrinkSatiationFuelNovakinCore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = entities.System<SharedSolutionContainerSystem>();
        var needs = entities.System<SharedNeedsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid foodNovakin = default;
        EntityUid drinkNovakin = default;
        EntityUid negativeNovakin = default;

        await server.WaitAssertion(() =>
        {
            foodNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            drinkNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.One, map.MapId));
            negativeNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.UnitX * 2, map.MapId));

            foreach (var novakin in new[] { foodNovakin, drinkNovakin, negativeNovakin })
            {
                Assert.That(needs.TrySetNeedLevel(novakin, NeedType.Fuel, 0f), Is.True);
                entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 400f;
            }

            AddChemical(foodNovakin, "Protein");
            AddChemical(drinkNovakin, "Water");
            AddChemical(negativeNovakin, "TableSalt");
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            AssertChemicalRemoved(foodNovakin, "Protein");
            AssertChemicalRemoved(drinkNovakin, "Water");
            AssertChemicalRemoved(negativeNovakin, "TableSalt");
            Assert.Multiple(() =>
            {
                Assert.That(needs.TryGetNeedLevel(foodNovakin, NeedType.Fuel), Is.GreaterThan(0f));
                Assert.That(needs.TryGetNeedLevel(drinkNovakin, NeedType.Fuel), Is.GreaterThan(0f));
                Assert.That(needs.TryGetNeedLevel(negativeNovakin, NeedType.Fuel), Is.EqualTo(0f));
                Assert.That(entities.GetComponent<TemperatureComponent>(drinkNovakin).CurrentTemperature,
                    Is.LessThan(entities.GetComponent<TemperatureComponent>(foodNovakin).CurrentTemperature));
            });
        });

        await pair.CleanReturnAsync();

        void AddChemical(EntityUid target, string reagent)
        {
            Assert.That(solutions.TryGetSolution(target, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var chemicalSolution, out _), Is.True);
            Assert.That(solutions.TryAddReagent(chemicalSolution!.Value, reagent, FixedPoint2.New(1)), Is.True);
        }

        void AssertChemicalRemoved(EntityUid target, string reagent)
        {
            Assert.That(solutions.TryGetSolution(target, BloodstreamComponent.DefaultChemicalsSolutionName,
                out _, out var chemicals), Is.True);
            Assert.That(chemicals!.ContainsReagent(reagent, null), Is.False);
        }
    }

    [Test]
    public async Task CoreClearsUnmetabolizedChemicalsAndAlcoholWithoutIntoxication()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = entities.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        Entity<SolutionComponent> chemicalSolution = default;
        Solution chemicals = default!;

        await server.WaitAssertion(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            Assert.That(solutions.TryGetSolution(novakin, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var resolvedSolution, out var resolvedChemicals), Is.True);
            chemicalSolution = resolvedSolution!.Value;
            chemicals = resolvedChemicals!;
            Assert.That(solutions.TryAddReagent(chemicalSolution, "Syrup", FixedPoint2.New(1)), Is.True);
            Assert.That(solutions.TryAddReagent(chemicalSolution, "Ethanol", FixedPoint2.New(1)), Is.True);
            Assert.That(solutions.TryAddReagent(chemicalSolution, "NovakinUnsupportedMetabolismReagent",
                FixedPoint2.New(1)), Is.True);
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(chemicals.ContainsReagent("Syrup", null), Is.False);
                Assert.That(chemicals.GetTotalPrototypeQuantity("Ethanol"), Is.LessThan(FixedPoint2.New(1)));
                Assert.That(chemicals.ContainsReagent("NovakinUnsupportedMetabolismReagent", null), Is.False);
                Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SmallAlcoholDoseHeatsNovakinCoreWithoutIntoxication()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = entities.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        EntityUid control = default;

        await server.WaitAssertion(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            control = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.One, map.MapId));
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 400f;
            entities.GetComponent<TemperatureComponent>(control).CurrentTemperature = 400f;
            Assert.That(solutions.TryGetSolution(novakin, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var chemicalSolution, out _), Is.True);
            Assert.That(solutions.TryAddReagent(chemicalSolution!.Value, "Moonshine", FixedPoint2.New(1)), Is.True);
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature,
                    Is.GreaterThan(entities.GetComponent<TemperatureComponent>(control).CurrentTemperature));
                Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelThresholdsPreserveThreeHourTankProportions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var fuel = prototypes.Index<NeedPrototype>("NeedNovakinFuel");
            Assert.Multiple(() =>
            {
                Assert.That(fuel.MinutesFromMaxToMin, Is.EqualTo(180f));
                Assert.That(fuel.SatisfiedMinutesFromFull, Is.EqualTo(108f));
                Assert.That(fuel.LowMinutesFromFull, Is.EqualTo(144f));
                Assert.That(fuel.CriticalMinutesFromFull, Is.EqualTo(180f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeatIntoxicationTracksCoreTemperature()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var statuses = entities.System<StatusEffectsSystem>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var temperature = entities.GetComponent<TemperatureComponent>(novakin);

            temperature.CurrentTemperature = 499.9f;
            physiologySystem.Update(0.5f);
            Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.False);

            temperature.CurrentTemperature = 500f;
            physiologySystem.Update(0.5f);
            Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.True);

            temperature.CurrentTemperature = 700f;
            physiologySystem.Update(0.5f);
            Assert.That(statuses.TryGetTime(novakin, SharedDrunkSystem.DrunkKey, out var peak), Is.True);
            Assert.That((peak!.Value.Item2 - peak.Value.Item1).TotalSeconds, Is.EqualTo(260d).Within(0.01d));

            temperature.CurrentTemperature = 600f;
            physiologySystem.Update(0.5f);
            Assert.That(statuses.TryGetTime(novakin, SharedDrunkSystem.DrunkKey, out var cooling), Is.True);
            Assert.That((cooling!.Value.Item2 - cooling.Value.Item1).TotalSeconds, Is.EqualTo(155d).Within(0.01d));

            temperature.CurrentTemperature = 499.9f;
            physiologySystem.Update(0.5f);
            Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SatiateThirstCoolingRemovesHeatIntoxication()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = entities.System<SharedSolutionContainerSystem>();
        var statuses = entities.System<StatusEffectsSystem>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;

        await server.WaitAssertion(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 501f;
            physiologySystem.Update(0.5f);
            Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.True);

            Assert.That(solutions.TryGetSolution(novakin, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var chemicals, out _), Is.True);
            Assert.That(solutions.TryAddReagent(chemicals!.Value, "Water", FixedPoint2.New(1)), Is.True);
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature, Is.LessThan(500f));
                Assert.That(entities.HasComponent<DrunkComponent>(novakin), Is.False);
                Assert.That(statuses.HasStatusEffect(novakin, "SlurredSpeech"), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RawProteinPoisonsHumanButNotNovakin()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = entities.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        EntityUid human = default;

        await server.WaitAssertion(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            human = entities.SpawnEntity("MobHuman", new MapCoordinates(Vector2.One, map.MapId));
            AddRawProtein(novakin);
            AddRawProtein(human);
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetDamage(entities.GetComponent<DamageableComponent>(novakin), "Poison"), Is.EqualTo(0f));
                Assert.That(GetDamage(entities.GetComponent<DamageableComponent>(human), "Poison"), Is.GreaterThan(0f));
            });
        });

        await pair.CleanReturnAsync();

        void AddRawProtein(EntityUid target)
        {
            Assert.That(solutions.TryGetSolution(target, BloodstreamComponent.DefaultChemicalsSolutionName,
                out var chemicals, out _), Is.True);
            Assert.That(solutions.TryAddReagent(chemicals!.Value, "UncookedAnimalProteins", FixedPoint2.New(1)), Is.True);
        }
    }

    [Test]
    public async Task PoisonImmunityFiltersOnlyPoisonFromBypassDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var damageable = entities.System<DamageableSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var human = entities.SpawnEntity("MobHuman", new MapCoordinates(Vector2.One, map.MapId));
            var source = new DamageSpecifier { DamageDict = { ["Poison"] = 10f, ["Blunt"] = 10f } };

            damageable.TryChangeDamage(novakin, source, ignoreResistances: true);
            damageable.TryChangeDamage(human, source, ignoreResistances: true);

            var novakinDamage = entities.GetComponent<DamageableComponent>(novakin);
            var humanDamage = entities.GetComponent<DamageableComponent>(human);
            Assert.Multiple(() =>
            {
                Assert.That(GetDamage(novakinDamage, "Poison"), Is.EqualTo(0f));
                Assert.That(GetDamage(novakinDamage, "Blunt"), Is.EqualTo(10f));
                Assert.That(GetDamage(humanDamage, "Poison"), Is.EqualTo(10f));
                Assert.That(source.DamageDict.ContainsKey("Poison"), Is.True);
            });
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
            var inhaler = entities.SpawnEntity("NovakinInhaler", coordinates);
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var tank = entities.GetComponent<GasTankComponent>(inhaler);
            var initialMoles = tank.Air.GetMoles(Gas.Nitrogen);

            physiology.CurrentReserve = 0f;
            entities.EventBus.RaiseLocalEvent(inhaler, new UseInHandEvent(novakin));

            Assert.That(physiology.CurrentReserve, Is.EqualTo(100f).Within(0.001f));
            Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.LessThan(initialMoles));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NitrogenInhalerDoesNotOverfillReserve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            var novakin = entities.SpawnEntity("MobNovakin", coordinates);
            var inhaler = entities.SpawnEntity("NovakinInhaler", coordinates);
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var tank = entities.GetComponent<GasTankComponent>(inhaler);
            physiology.CurrentReserve = 90f;

            entities.EventBus.RaiseLocalEvent(inhaler, new UseInHandEvent(novakin));

            Assert.Multiple(() =>
            {
                Assert.That(physiology.CurrentReserve, Is.EqualTo(100f).Within(0.001f));
                Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.EqualTo(0.245782035f).Within(0.001f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LargeNitrogenInhalerUsesDoubleEmergencyTankCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inhaler = entities.SpawnEntity("NovakinInhalerLarge", new MapCoordinates(Vector2.Zero, map.MapId));
            var tank = entities.GetComponent<GasTankComponent>(inhaler);
            var inhalerComponent = entities.GetComponent<NovakinInhalerComponent>(inhaler);

            Assert.Multiple(() =>
            {
                Assert.That(tank.Air.Volume, Is.EqualTo(2.5f).Within(0.001f));
                Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.EqualTo(1.025689525f).Within(0.000001f));
                Assert.That(inhalerComponent.MaxMoles, Is.EqualTo(1.025689525f).Within(0.000001f));
            });
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
    public async Task CompromisedShellThermalDamageScalesWithNitrogenReserve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var damageable = entities.System<DamageableSystem>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            var cases = new[]
            {
                (Reserve: 100f, ExpectedRelativeDamage: 10f),
                (Reserve: 50f, ExpectedRelativeDamage: 5f),
                (Reserve: 0f, ExpectedRelativeDamage: 1f),
            };
            var novakins = new List<(EntityUid Uid, float ExpectedRelativeDamage, float InitialHeatDamage)>();

            foreach (var (reserve, expectedRelativeDamage) in cases)
            {
                var novakin = entities.SpawnEntity("MobNovakin", coordinates);
                var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
                physiology.CurrentReserve = reserve;
                entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 700f;
                damageable.TryChangeDamage(novakin, new DamageSpecifier { DamageDict = { ["Blunt"] = 100f } });
                var initialHeatDamage = GetDamage(entities.GetComponent<DamageableComponent>(novakin), "Heat");
                novakins.Add((novakin, expectedRelativeDamage, initialHeatDamage));
            }

            physiologySystem.Update(0.5f);

            var emptyDamage = novakins.Single(novakin => novakin.ExpectedRelativeDamage == 1f);
            var emptyDamageDelta = GetDamage(entities.GetComponent<DamageableComponent>(emptyDamage.Uid), "Heat")
                - emptyDamage.InitialHeatDamage;
            foreach (var (novakin, expectedRelativeDamage, initialHeatDamage) in novakins)
            {
                var damage = entities.GetComponent<DamageableComponent>(novakin);
                var damageDelta = GetDamage(damage, "Heat") - initialHeatDamage;
                Assert.That(damageDelta, Is.EqualTo(emptyDamageDelta * expectedRelativeDamage).Within(0.03f));
            }
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
            var protectedPhysiology = entities.GetComponent<NovakinPhysiologyComponent>(protectedNovakin);
            var protectedLost = 100f - protectedPhysiology.CurrentReserve;
            Assert.Multiple(() =>
            {
                Assert.That(unprotectedLost, Is.GreaterThan(0f));
                Assert.That(protectedLost, Is.EqualTo(unprotectedLost
                    * protectedPhysiology.PressureSuitShellFailureReserveDrainMultiplier).Within(0.001f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VacuumBarotraumaIsTwentyFivePercentSlowerThanOrganicHumanoids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var barotrauma = entities.System<BarotraumaSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            barotrauma.Update(1f);

            var damage = entities.GetComponent<DamageableComponent>(novakin);
            Assert.That(GetDamage(damage, "Blunt"), Is.EqualTo(1.92f).Within(0.001f));
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
    public async Task SsdCriticalNovakinStillTakesThermalDamageAndBloodloss()
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
            var activeNovakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.One, map.MapId));
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var mind = entities.GetComponent<MindContainerComponent>(novakin);
            mind.Mind = novakin;
            entities.GetComponent<SSDIndicatorComponent>(novakin).IsSSD = true;
            physiology.CurrentReserve = 12f;
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 1000f;
            damageable.TryChangeDamage(novakin, new DamageSpecifier { DamageDict = { ["Blunt"] = 100f } });
            damageable.TryChangeDamage(activeNovakin, new DamageSpecifier { DamageDict = { ["Blunt"] = 100f } });

            physiologySystem.Update(5f);

            var damage = entities.GetComponent<DamageableComponent>(novakin);
            var activeLoss = 100f - entities.GetComponent<NovakinPhysiologyComponent>(activeNovakin).CurrentReserve;
            Assert.Multiple(() =>
            {
                Assert.That(physiology.CurrentReserve, Is.LessThan(12f));
                Assert.That(12f - physiology.CurrentReserve, Is.EqualTo(activeLoss).Within(0.001f));
                Assert.That(GetDamage(damage, "Heat"), Is.GreaterThan(0f));
                Assert.That(GetDamage(damage, "Bloodloss"), Is.GreaterThan(0f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeadSsdNovakinContinuesVentingAndThermalFailure()
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
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            entities.GetComponent<MindContainerComponent>(novakin).Mind = novakin;
            entities.GetComponent<SSDIndicatorComponent>(novakin).IsSSD = true;
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 1000f;
            damageable.TryChangeDamage(novakin, new DamageSpecifier { DamageDict = { ["Blunt"] = 200f } });

            physiologySystem.Update(1f);

            var damage = entities.GetComponent<DamageableComponent>(novakin);
            Assert.Multiple(() =>
            {
                Assert.That(physiology.ShellShattered, Is.True);
                Assert.That(physiology.CurrentReserve, Is.LessThan(100f));
                Assert.That(GetDamage(damage, "Heat"), Is.GreaterThan(0f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SixtyUnitsOfFlammableReagentQueuePeakCoreHeatGradually()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var temperature = entities.GetComponent<TemperatureComponent>(novakin);
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            temperature.CurrentTemperature = physiology.FuelConsumptionBaselineTemperature;

            entities.EventBus.RaiseLocalEvent(novakin,
                new ReagentMetabolizedEvent(prototypes.Index<ReagentPrototype>("Moonshine"), FixedPoint2.New(60)));
            Assert.That(physiology.PendingReagentHeat,
                Is.EqualTo(700f - physiology.FuelConsumptionBaselineTemperature).Within(0.001f));

            physiologySystem.Update(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(temperature.CurrentTemperature,
                    Is.GreaterThan(physiology.FuelConsumptionBaselineTemperature).And.LessThan(380f));
                Assert.That(physiology.PendingReagentHeat, Is.GreaterThan(300f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HealthAnalyzerReportsNovakinNitrogenReserve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var analyzer = entities.System<HealthAnalyzerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            entities.GetComponent<NovakinPhysiologyComponent>(novakin).CurrentReserve = 37f;

            var state = analyzer.GetHealthAnalyzerUiState(EntityUid.Invalid, novakin);
            Assert.That(state.NitrogenReserve, Is.EqualTo(0.37f).Within(0.001f));
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
                Assert.That(vision.HeatSaturation, Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(vision.HeatWashout, Is.LessThan(0.2125f));
            });

            temperature.CurrentTemperature = 700f;
            physiologySystem.Update(0.5f);
            Assert.Multiple(() =>
            {
                Assert.That(vision.HeatSaturation, Is.EqualTo(1f).Within(0.001f));
                Assert.That(vision.HeatWashout, Is.EqualTo(0.2125f).Within(0.001f));
            });
        });

        await pair.CleanReturnAsync();
    }

    private static float GetDamage(DamageableComponent damageable, string damageType)
        => damageable.Damage.DamageDict.TryGetValue(damageType, out var damage) ? damage.Float() : 0f;
}
