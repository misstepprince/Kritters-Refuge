using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._Kritters.Novakin.Adapters.KrittersBloodTypes;
using Content.Server._Kritters.Novakin.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._DV.Chemistry.Components;
using Content.Shared._Kritters.Novakin.Components;
using Content.Shared._Kritters.Novakin.EntityEffects;
using Content.Shared._Kritters.Novakin.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Alert;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.EntityEffects;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Speech;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Server.GameObjects;

namespace Content.IntegrationTests.Tests._Kritters.Novakin;

[TestFixture]
public sealed class NovakinFoundationTest
{
    private static readonly string[] GasIds =
    {
        "NovakinGasNitrogen",
        "NovakinGasOxygen",
        "NovakinGasNitrousOxide",
        "NovakinGasAmmonia",
        "NovakinGasCarbonDioxide",
        "NovakinGasWaterVapor",
    };

    [Test]
    public async Task SpeciesFoundationTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var bodySystem = entities.System<BodySystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;

        await server.WaitAssertion(() =>
        {
            foreach (var gasId in GasIds)
                Assert.That(prototypes.HasIndex<NovakinGasPrototype>(gasId), Is.True, $"Missing gas {gasId}");

            Assert.That(prototypes.EnumeratePrototypes<NovakinGasPrototype>().Count(), Is.EqualTo(6));

            Assert.That(GetNovakinHeatDelta(prototypes.Index<ReagentPrototype>("Ethanol"), "Drink"), Is.EqualTo(1f));
            Assert.That(GetNovakinHeatDelta(prototypes.Index<ReagentPrototype>("WeldingFuel"), "Fuel"), Is.EqualTo(2f));
            Assert.That(GetNovakinHeatDelta(prototypes.Index<ReagentPrototype>("Oil"), "Fuel"), Is.EqualTo(1.5f));
            Assert.That(GetNovakinHeatDelta(prototypes.Index<ReagentPrototype>("OilOlive"), "Fuel"), Is.EqualTo(1.5f));

            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<NovakinPhysiologyComponent>(novakin), Is.True);
                Assert.That(entities.HasComponent<AtmosExposedComponent>(novakin), Is.True);
                Assert.That(entities.HasComponent<TemperatureComponent>(novakin), Is.True);
                Assert.That(entities.HasComponent<PointLightComponent>(novakin), Is.True);
                Assert.That(entities.HasComponent<BlockInjectionComponent>(novakin), Is.True);
                Assert.That(entities.HasComponent<BloodstreamComponent>(novakin), Is.False);
                Assert.That(entities.HasComponent<RespiratorComponent>(novakin), Is.False);
            });

            var tags = entities.GetComponent<TagComponent>(novakin).Tags;
            Assert.That(tags, Does.Contain("DoorBumpOpener"));
            Assert.That(tags, Does.Contain("CanPilot"));
            Assert.That(tags, Does.Contain("FootstepSound"));
            Assert.That(tags, Does.Contain("AnomalyHost"));

            var speech = entities.GetComponent<SpeechComponent>(novakin);
            Assert.That(speech.SpeechVerb.Id, Is.EqualTo("Novakin"));
            Assert.That(speech.SpeechSounds!.Value.Id, Is.EqualTo("Novakin"));
            Assert.Multiple(() =>
            {
                Assert.That(speech.SuffixSpeechVerbs["chat-speech-verb-suffix-question"].Id,
                    Is.EqualTo("DefaultQuestion"));
                Assert.That(speech.SuffixSpeechVerbs["chat-speech-verb-suffix-exclamation"].Id,
                    Is.EqualTo("DefaultExclamation"));
                Assert.That(speech.SuffixSpeechVerbs["chat-speech-verb-suffix-exclamation-strong"].Id,
                    Is.EqualTo("DefaultExclamationStrong"));
                Assert.That(speech.SuffixSpeechVerbs["chat-speech-verb-suffix-stutter"].Id,
                    Is.EqualTo("DefaultStutter"));
                Assert.That(speech.SuffixSpeechVerbs["chat-speech-verb-suffix-mumble"].Id,
                    Is.EqualTo("DefaultMumble"));
            });

            var organs = bodySystem.GetBodyOrgans(novakin)
                .Select(organ => entities.GetComponent<MetaDataComponent>(organ.Id).EntityPrototype?.ID)
                .ToArray();

            Assert.That(organs, Is.EquivalentTo(new[] { "OrganNovakinNexus", "OrganNovakinCore" }));

            var core = bodySystem.GetBodyOrgans(novakin)
                .Single(organ => entities.GetComponent<MetaDataComponent>(organ.Id).EntityPrototype?.ID == "OrganNovakinCore");
            Assert.That(entities.HasComponent<StomachComponent>(core.Id), Is.True);
            var metabolizer = entities.GetComponent<MetabolizerComponent>(core.Id);
            Assert.That(metabolizer.MetabolizerTypes, Does.Contain("Human"));
            Assert.That(metabolizer.MetabolismGroups!.Select(group => group.Id.Id),
                Does.Contain("Food").And.Contain("Drink").And.Contain("Medicine").And.Contain("Poison"));
        });

        await pair.CleanReturnAsync();
    }

    private static float GetNovakinHeatDelta(ReagentPrototype reagent, string metabolismGroup)
    {
        return reagent.Metabolisms![metabolismGroup].Effects
            .OfType<NovakinHeat>()
            .Single()
            .TemperatureDelta;
    }

    [Test]
    public async Task NovakinHeatEffectTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        EntityUid human = default;

        await server.WaitAssertion(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            human = entities.SpawnEntity("MobHuman", new MapCoordinates(Vector2.One, map.MapId));

            var novakinTemperature = entities.GetComponent<TemperatureComponent>(novakin);
            var humanTemperature = entities.GetComponent<TemperatureComponent>(human);
            var humanInitialTemperature = humanTemperature.CurrentTemperature;

            var heat = new NovakinHeat { TemperatureDelta = 1000f };
            heat.Effect(new EntityEffectBaseArgs(novakin, entities));
            heat.Effect(new EntityEffectBaseArgs(human, entities));

            Assert.That(novakinTemperature.CurrentTemperature, Is.EqualTo(700f));
            Assert.That(humanTemperature.CurrentTemperature, Is.EqualTo(humanInitialTemperature));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NovakinFuelTemperatureCapIsBelowHeatDamageThreshold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            var physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            var temperature = entities.GetComponent<TemperatureComponent>(novakin);

            Assert.That(temperature.HeatDamageThreshold, Is.GreaterThan(physiology.FuelConsumptionMaximumTemperature));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NovakinIsImmuneToPoisonDamage()
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

            damageable.TryChangeDamage(novakin, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Poison"), 10));
            Assert.That(damage.TotalDamage, Is.EqualTo(FixedPoint2.Zero));

            damageable.TryChangeDamage(novakin, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 10));
            Assert.That(damage.TotalDamage, Is.EqualTo(FixedPoint2.New(10)));

            damageable.TryChangeDamage(novakin, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Heat"), 10));
            Assert.That(damage.TotalDamage, Is.EqualTo(FixedPoint2.New(12.5f)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NovakinGlowScalesWithFuelTemperature()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        NovakinPhysiologyComponent physiology = default!;
        PointLightComponent light = default!;
        TemperatureComponent temperature = default!;

        await server.WaitPost(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            light = entities.GetComponent<PointLightComponent>(novakin);
            temperature = entities.GetComponent<TemperatureComponent>(novakin);
            temperature.CurrentTemperature = physiology.MinimumGlowTemperature;
        });

        // Novakin physiology updates on a 0.5-second cadence.
        await server.WaitRunTicks(30);
        float baseEnergy = 0f;
        await server.WaitAssertion(() =>
        {
            baseEnergy = light.Energy;
            Assert.That(baseEnergy, Is.EqualTo(physiology.MinimumGlowEnergy).Within(0.001f));
            Assert.That(physiology.GlowIntensity,
                Is.EqualTo(physiology.MinimumGlowEnergy / physiology.FullGlowEnergy).Within(0.001f));
        });

        await server.WaitPost(() =>
            temperature.CurrentTemperature = (physiology.MinimumGlowTemperature + physiology.FuelConsumptionMaximumTemperature) / 2f);
        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(light.Energy, Is.GreaterThan(baseEnergy));
            Assert.That(light.Energy, Is.LessThan(physiology.FullGlowEnergy));
        });

        await server.WaitPost(() => temperature.CurrentTemperature = physiology.FuelConsumptionMaximumTemperature);
        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            // Native thermal regulation may cool the mob before the next
            // half-second physiology update. Validate the light against the
            // temperature that was actually processed instead of assuming it
            // remains at the requested maximum.
            var temperatureRange = physiology.FuelConsumptionMaximumTemperature - physiology.MinimumGlowTemperature;
            var temperatureFactor = temperatureRange > 0f
                ? Math.Clamp((temperature.CurrentTemperature - physiology.MinimumGlowTemperature) / temperatureRange, 0f, 1f)
                : 1f;
            var expectedEnergy = MathHelper.Lerp(
                physiology.MinimumGlowEnergy,
                physiology.FullGlowEnergy,
                temperatureFactor);

            Assert.That(light.Energy, Is.EqualTo(expectedEnergy).Within(0.01f));
            Assert.That(physiology.GlowIntensity,
                Is.EqualTo(expectedEnergy / physiology.FullGlowEnergy).Within(0.01f));
            Assert.That(physiology.MaximumBodyGlowOpacity, Is.EqualTo(0.85f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThermalContainmentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var mapLoader = entities.System<MapLoaderSystem>();
        var mapSystem = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        EntityUid grid = default;
        EntityUid novakin = default;
        TemperatureComponent temperature = default!;
        GasMixture mixture = default!;
        float initialAtmosTemperature = 0f;

        await server.WaitPost(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var mapPath = new ResPath("Maps/Test/Breathing/3by3-20oxy-80nit.yml");
            Assert.That(mapLoader.TryLoadGrid(mapId, mapPath, out var gridEntity), Is.True);
            grid = gridEntity!.Value.Owner;

            novakin = entities.SpawnEntity("MobNovakin", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            temperature = entities.GetComponent<TemperatureComponent>(novakin);
            mixture = entities.System<AtmosphereSystem>().GetContainingMixture(novakin)!;
            initialAtmosTemperature = mixture.Temperature;
        });

        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(temperature.CurrentTemperature, Is.LessThan(373.15f));
            Assert.That(mixture.Temperature, Is.GreaterThan(initialAtmosTemperature));
        });

        await server.WaitPost(() =>
        {
            var coordinates = entities.GetComponent<TransformComponent>(novakin).Coordinates;
            var suit = entities.SpawnEntity("ClothingOuterSuitFire", coordinates);
            Assert.That(inventory.TryEquip(novakin, suit, "outerClothing", force: true), Is.True);
            temperature.CurrentTemperature = 350f;
        });

        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(temperature.CurrentTemperature, Is.GreaterThan(350f));
            Assert.That(temperature.CurrentTemperature, Is.LessThanOrEqualTo(373.15f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReserveDepletionTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var alerts = entities.System<AlertsSystem>();
        var mobState = entities.System<MobStateSystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        NovakinPhysiologyComponent physiology = default!;

        await server.WaitPost(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            physiology.CurrentReserve = 100f;
            physiology.ReserveDrainPerSecond = 10f;
        });

        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(physiology.CurrentReserve, Is.LessThan(100f));
            Assert.That(alerts.IsShowingAlert(novakin, physiology.ReserveAlert), Is.True);
        });

        float pausedReserve = 0f;
        await server.WaitPost(() =>
        {
            entities.AddComponent<CryostorageContainedComponent>(novakin);
            physiology.CurrentReserve = 10f;
            pausedReserve = 10f;
        });
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(physiology.CurrentReserve, Is.EqualTo(pausedReserve).Within(0.001f));
            var reserveAlert = alerts.GetActiveAlerts(novakin)!.Values
                .Single(state => state.Type == physiology.ReserveAlert);
            Assert.That(reserveAlert.Severity, Is.EqualTo(1));
        });

        float criticalReserve = 0f;
        await server.WaitPost(() =>
        {
            entities.RemoveComponent<CryostorageContainedComponent>(novakin);
            mobState.ChangeMobState(novakin, MobState.Critical);
            physiology.CurrentReserve = 10f;
            criticalReserve = physiology.CurrentReserve;
        });
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
            Assert.That(physiology.CurrentReserve, Is.LessThan(criticalReserve)));

        await server.WaitPost(() =>
        {
            mobState.ChangeMobState(novakin, MobState.Dead);
            pausedReserve = physiology.CurrentReserve;
        });
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
            Assert.That(physiology.CurrentReserve, Is.EqualTo(pausedReserve).Within(0.001f)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CriticalHealthAcceleratesReserveLossTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var mobState = entities.System<MobStateSystem>();
        var physiologySystem = entities.System<NovakinPhysiologySystem>();
        var map = await pair.CreateTestMap();
        NovakinPhysiologyComponent healthy = default!;
        NovakinPhysiologyComponent critical = default!;

        await server.WaitPost(() =>
        {
            var healthyUid = entities.SpawnEntity("MobNovakin", new MapCoordinates(new Vector2(-1f, 0f), map.MapId));
            var criticalUid = entities.SpawnEntity("MobNovakin", new MapCoordinates(new Vector2(1f, 0f), map.MapId));
            healthy = entities.GetComponent<NovakinPhysiologyComponent>(healthyUid);
            critical = entities.GetComponent<NovakinPhysiologyComponent>(criticalUid);
            healthy.ReserveDrainPerSecond = 10f;
            critical.ReserveDrainPerSecond = 10f;
            mobState.ChangeMobState(criticalUid, MobState.Critical);
            physiologySystem.Update(0.5f);
        });

        await server.WaitAssertion(() =>
            Assert.That(critical.CurrentReserve, Is.LessThan(healthy.CurrentReserve)));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReserveThermalContainmentCurveTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        NovakinPhysiologyComponent physiology = default!;
        ThermalRegulatorComponent regulator = default!;

        await server.WaitPost(() =>
        {
            var novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            regulator = entities.GetComponent<ThermalRegulatorComponent>(novakin);
            physiology.ReserveDrainPerSecond = 0f;
            physiology.CurrentReserve = 25f;
        });

        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(regulator.ImplicitHeatRegulation, Is.EqualTo(300f).Within(0.001f));
            Assert.That(regulator.SweatHeatRegulation, Is.EqualTo(300f).Within(0.001f));
            Assert.That(regulator.ShiveringHeatRegulation, Is.EqualTo(300f).Within(0.001f));
        });

        await server.WaitPost(() => physiology.CurrentReserve = 0f);
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(regulator.ImplicitHeatRegulation, Is.EqualTo(125f).Within(0.001f));
            Assert.That(regulator.SweatHeatRegulation, Is.EqualTo(125f).Within(0.001f));
            Assert.That(regulator.ShiveringHeatRegulation, Is.EqualTo(125f).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InhalerRequiresMatchingGasTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        EntityUid nitrogenInhaler = default;
        EntityUid oxygenInhaler = default;
        EntityUid nitrogenTankSource = default;
        NovakinPhysiologyComponent physiology = default!;
        GasTankComponent nitrogenTank = default!;
        GasTankComponent oxygenTank = default!;
        float initialNitrogenMoles = 0f;
        float initialOxygenMoles = 0f;

        await server.WaitPost(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            novakin = entities.SpawnEntity("MobNovakin", coordinates);
            nitrogenInhaler = entities.SpawnEntity("NovakinInhalerNitrogen", coordinates);
            oxygenInhaler = entities.SpawnEntity("NovakinInhalerOxygen", coordinates);
            physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            nitrogenTank = entities.GetComponent<GasTankComponent>(nitrogenInhaler);
            oxygenTank = entities.GetComponent<GasTankComponent>(oxygenInhaler);
            physiology.CurrentReserve = 0f;
            initialNitrogenMoles = nitrogenTank.Air.GetMoles(Gas.Nitrogen);
            initialOxygenMoles = oxygenTank.Air.GetMoles(Gas.Oxygen);

            entities.EventBus.RaiseLocalEvent(nitrogenInhaler, new UseInHandEvent(novakin));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(physiology.CurrentReserve, Is.EqualTo(10f).Within(0.001f));
            Assert.That(nitrogenTank.Air.GetMoles(Gas.Nitrogen), Is.LessThan(initialNitrogenMoles));
        });

        await server.WaitPost(() =>
        {
            physiology.CurrentReserve = 0f;
            entities.EventBus.RaiseLocalEvent(oxygenInhaler, new UseInHandEvent(novakin));
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(physiology.CurrentReserve, Is.Zero.Within(0.001f));
            Assert.That(oxygenTank.Air.GetMoles(Gas.Oxygen), Is.EqualTo(initialOxygenMoles).Within(0.0001f));
        });

        await server.WaitRunTicks(60);
        float sourceMoles = 0f;
        await server.WaitPost(() =>
        {
            var coordinates = entities.GetComponent<TransformComponent>(novakin).Coordinates;
            nitrogenTankSource = entities.SpawnEntity("EmergencyNitrogenTankFilled", coordinates);
            var sourceTank = entities.GetComponent<GasTankComponent>(nitrogenTankSource);
            nitrogenTank.Air.SetMoles(Gas.Nitrogen, 0f);
            sourceMoles = sourceTank.Air.GetMoles(Gas.Nitrogen);

            entities.EventBus.RaiseLocalEvent(nitrogenInhaler,
                new AfterInteractEvent(novakin, nitrogenInhaler, nitrogenTankSource, coordinates, true));
        });
        await server.WaitAssertion(() =>
        {
            var sourceTank = entities.GetComponent<GasTankComponent>(nitrogenTankSource);
            Assert.That(nitrogenTank.Air.GetMoles(Gas.Nitrogen), Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(sourceTank.Air.GetMoles(Gas.Nitrogen), Is.LessThan(sourceMoles));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InhalerHasFiveTenReserveUsesAndSupportsPartialTransfersTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        EntityUid inhaler = default;
        NovakinPhysiologyComponent physiology = default!;
        GasTankComponent tank = default!;

        await server.WaitPost(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            novakin = entities.SpawnEntity("MobNovakin", coordinates);
            inhaler = entities.SpawnEntity("NovakinInhalerNitrogen", coordinates);
            physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            tank = entities.GetComponent<GasTankComponent>(inhaler);
            Assert.That(tank.Air.GetMoles(Gas.Nitrogen) * 400f, Is.EqualTo(50f).Within(0.001f));
        });

        for (var use = 1; use <= 5; use++)
        {
            if (use > 1)
                await server.WaitRunTicks(60);

            await server.WaitPost(() =>
            {
                physiology.CurrentReserve = 0f;
                entities.EventBus.RaiseLocalEvent(inhaler, new UseInHandEvent(novakin));
            });
            await server.WaitAssertion(() => Assert.That(physiology.CurrentReserve, Is.EqualTo(10f).Within(0.001f)));
        }

        await server.WaitAssertion(() => Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.Zero.Within(0.0001f)));

        await server.WaitPost(() =>
        {
            inhaler = entities.SpawnEntity("NovakinInhalerNitrogen", new MapCoordinates(Vector2.Zero, map.MapId));
            tank = entities.GetComponent<GasTankComponent>(inhaler);
            physiology.CurrentReserve = 95f;
            entities.EventBus.RaiseLocalEvent(inhaler, new UseInHandEvent(novakin));
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(physiology.CurrentReserve, Is.EqualTo(100f).Within(0.001f));
            Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.EqualTo(0.1125f).Within(0.0001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SurvivalKitInhalerAdaptsToSelectedGasTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var transform = entities.System<SharedTransformSystem>();
        var adapter = entities.System<NovakinBloodTypeAdapterSystem>();
        var map = await pair.CreateTestMap();
        EntityUid inhaler = default;

        await server.WaitPost(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            var novakin = entities.SpawnEntity("MobNovakin", coordinates);
            var survivalKit = entities.SpawnEntity("NovakinBoxSurvival", coordinates);
            transform.SetParent(survivalKit, novakin);

            var storage = entities.GetComponent<StorageComponent>(survivalKit);
            inhaler = storage.Container.ContainedEntities
                .Single(uid => entities.HasComponent<NovakinInhalerComponent>(uid));

            adapter.ConfigureStartingInhalers(novakin, "NovakinGasAmmonia");
        });

        await server.WaitAssertion(() =>
        {
            var inhalerComponent = entities.GetComponent<NovakinInhalerComponent>(inhaler);
            var tank = entities.GetComponent<GasTankComponent>(inhaler);
            Assert.That(inhalerComponent.Gas.Id, Is.EqualTo("NovakinGasAmmonia"));
            Assert.That(tank.Air.GetMoles(Gas.Ammonia), Is.EqualTo(inhalerComponent.MaxMoles).Within(0.0001f));
            Assert.That(tank.Air.GetMoles(Gas.Nitrogen), Is.Zero.Within(0.0001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CharacterColoredGlowTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var mobState = entities.System<MobStateSystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        NovakinPhysiologyComponent physiology = default!;
        PointLightComponent light = default!;
        var selectedColor = Color.FromHex("#42D9C8");

        await server.WaitPost(() =>
        {
            novakin = entities.SpawnEntity("MobNovakin", new MapCoordinates(Vector2.Zero, map.MapId));
            physiology = entities.GetComponent<NovakinPhysiologyComponent>(novakin);
            light = entities.GetComponent<PointLightComponent>(novakin);
            entities.GetComponent<HumanoidAppearanceComponent>(novakin).SkinColor = selectedColor;
            entities.GetComponent<TemperatureComponent>(novakin).CurrentTemperature = 350f;
        });

        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(light.Color, Is.EqualTo(selectedColor));
                Assert.That(light.Radius, Is.EqualTo(2f));
                // The unprotected test mob cools rapidly, but its colored glow remains at the configured floor.
                Assert.That(light.Energy, Is.EqualTo(physiology.MinimumGlowEnergy).Within(0.001f));
                Assert.That(physiology.GlowIntensity,
                    Is.EqualTo(light.Energy / physiology.FullGlowEnergy).Within(0.001f));
            });
        });

        await server.WaitPost(() => mobState.ChangeMobState(novakin, MobState.Dead));
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(light.Color, Is.EqualTo(selectedColor));
            Assert.That(light.Energy, Is.EqualTo(physiology.DeadGlowEnergy).Within(0.001f));
            Assert.That(physiology.GlowIntensity,
                Is.EqualTo(physiology.DeadGlowEnergy / physiology.FullGlowEnergy).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PressureSuitAndHelmetSuppressGlowTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var inventory = entities.System<InventorySystem>();
        var map = await pair.CreateTestMap();
        EntityUid novakin = default;
        EntityUid helmet = default;
        PointLightComponent light = default!;

        await server.WaitPost(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);
            novakin = entities.SpawnEntity("MobNovakin", coordinates);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitEVA", coordinates);
            helmet = entities.SpawnEntity("ClothingHeadHelmetEVA", coordinates);
            light = entities.GetComponent<PointLightComponent>(novakin);
            Assert.That(inventory.TryEquip(novakin, suit, "outerClothing", force: true), Is.True);
            Assert.That(inventory.TryEquip(novakin, helmet, "head", force: true), Is.True);
        });

        await server.WaitRunTicks(60);
        await server.WaitAssertion(() => Assert.That(light.Energy, Is.Zero.Within(0.001f)));

        await server.WaitPost(() => Assert.That(inventory.TryUnequip(novakin, "head", out _, force: true), Is.True));
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() => Assert.That(light.Energy, Is.GreaterThan(0f)));

        await pair.CleanReturnAsync();
    }
}
