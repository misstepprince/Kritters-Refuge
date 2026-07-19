using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Charges.Systems;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Construction;

public sealed class RCDTest : InteractionTest
{
    private static readonly EntProtoId Rcd = "RCD";
    private static readonly ProtoId<RCDPrototype> SettingDeconstruct = "Deconstruct";
    private static readonly ProtoId<RCDPrototype> SettingDeconstructTile = "DeconstructTile";
    private static readonly ProtoId<RCDPrototype> SettingDeconstructLattice = "DeconstructLattice";
    private static readonly ProtoId<RCDPrototype> SettingPlating = "Plating";
    private static readonly ProtoId<RCDPrototype> SettingFloorSteel = "FloorSteel";
    private static readonly ProtoId<RCDPrototype> SettingWallSolid = "WallSolid";
    private static readonly ProtoId<RCDPrototype> SettingWindowDirectional = "WindowDirectional";

    [TestPrototypes]
    private const string DirectionalRcdPrototypes = @"
- type: entity
  id: RCDTestNorth
  parent: RCD
  components:
  - type: RCD
    availablePrototypes: [WindowDirectional]
    constructionDirection: North

- type: entity
  id: RCDTestEast
  parent: RCD
  components:
  - type: RCD
    availablePrototypes: [WindowDirectional]
    constructionDirection: East
";

    [Test]
    public async Task ResolvedCostsAndTileLayersArePreserved()
    {
        var rcd = await PlaceInHands(Rcd);
        var charges = SEntMan.System<SharedChargesSystem>();
        var deconstructTile = ProtoMan.Index(SettingDeconstructTile);
        var deconstructLattice = ProtoMan.Index(SettingDeconstructLattice);
        var plating = ProtoMan.Index(SettingPlating);
        var floorSteel = ProtoMan.Index(SettingFloorSteel);

        await SetRcdProto(rcd, SettingDeconstruct);
        await SetCharges(rcd, deconstructTile.Cost - 1);
        await Interact(null, TargetCoords);
        await RunSeconds(deconstructTile.Delay + 0.5f);
        await AssertTile("Plating");
        Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.EqualTo(deconstructTile.Cost - 1));

        await SetCharges(rcd, deconstructTile.Cost);
        await Interact(null, TargetCoords);
        await AssertTile("Lattice");
        Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.Zero);

        await SetCharges(rcd, deconstructLattice.Cost);
        await Interact(null, TargetCoords);
        await AssertTile(null);
        Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.Zero);

        await SetTile("Lattice", grid: MapData.Grid);
        await SetRcdProto(rcd, SettingPlating);
        await SetCharges(rcd, plating.Cost);
        await Interact(null, TargetCoords);
        await AssertTile("Plating");

        await SetRcdProto(rcd, SettingFloorSteel);
        await SetCharges(rcd, floorSteel.Cost);
        await Interact(null, TargetCoords);
        await AssertTile("FloorSteel");

        await SetRcdProto(rcd, SettingDeconstruct);
        await SetCharges(rcd, deconstructTile.Cost);
        await Interact(null, TargetCoords);
        await AssertTile("Plating");
        Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.Zero);
    }

    [Test]
    public async Task DuplicateConstructionAndObjectDeconstructionUseExactCosts()
    {
        var rcd = await PlaceInHands(Rcd);
        var charges = SEntMan.System<SharedChargesSystem>();
        var wall = ProtoMan.Index(SettingWallSolid);
        const int initialCharges = 10;

        await SetRcdProto(rcd, SettingWallSolid);
        await SetCharges(rcd, initialCharges);
        await Interact(null, TargetCoords, awaitDoAfters: false);
        await Interact(null, TargetCoords, awaitDoAfters: false);
        await RunSeconds(wall.Delay + 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(CountPrototype(wall.Prototype!), Is.EqualTo(1));
            Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.EqualTo(initialCharges - wall.Cost));
        });

        await Interact(null, TargetCoords);
        Assert.Multiple(() =>
        {
            Assert.That(CountPrototype(wall.Prototype!), Is.EqualTo(1));
            Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.EqualTo(initialCharges - wall.Cost));
        });

        var wallUid = await FindEntity(wall.Prototype!);
        var deconstructable = SEntMan.GetComponent<RCDDeconstructableComponent>(wallUid);
        await SetRcdProto(rcd, SettingDeconstruct);
        await SetCharges(rcd, deconstructable.Cost - 1);
        await Interact(SEntMan.GetNetEntity(wallUid), TargetCoords);
        await RunSeconds(deconstructable.Delay + 0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.EntityExists(wallUid), Is.True);
            Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.EqualTo(deconstructable.Cost - 1));
        });

        await SetCharges(rcd, deconstructable.Cost);
        await Interact(SEntMan.GetNetEntity(wallUid), TargetCoords);
        await RunTicks(2);
        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.EntityExists(wallUid), Is.False);
            Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.Zero);
        });

        var secondCoordinates = Transform.WithEntityId(
            new EntityCoordinates(SPlayer, new Vector2(0, 1)),
            MapData.Grid);
        var secondNetCoordinates = SEntMan.GetNetCoordinates(secondCoordinates);
        await SetTile("Plating", secondNetCoordinates, MapData.Grid);
        await SetRcdProto(rcd, SettingWallSolid);
        await SetCharges(rcd, wall.Cost * 2);
        await Interact(null, TargetCoords, awaitDoAfters: false);
        await Interact(null, secondNetCoordinates, awaitDoAfters: false);
        await RunSeconds(wall.Delay + 0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(CountPrototype(wall.Prototype!), Is.EqualTo(2));
            Assert.That(charges.GetCurrentCharges(ToServer(rcd)), Is.Zero);
        });
    }

    [Test]
    public async Task DirectionalStructuresShareTileWithoutExactDuplicates()
    {
        var setting = ProtoMan.Index(SettingWindowDirectional);

        await PlaceInHands("RCDTestNorth");
        await Interact(null, TargetCoords);
        Assert.That(CountPrototype(setting.Prototype!), Is.EqualTo(1));

        var eastRcd = await PlaceInHands("RCDTestEast");
        var charges = SEntMan.System<SharedChargesSystem>();
        var initialCharges = charges.GetCurrentCharges(ToServer(eastRcd));
        await Interact(null, TargetCoords);
        Assert.Multiple(() =>
        {
            Assert.That(CountPrototype(setting.Prototype!), Is.EqualTo(2));
            Assert.That(charges.GetCurrentCharges(ToServer(eastRcd)), Is.EqualTo(initialCharges - setting.Cost));
        });

        await Interact(null, TargetCoords);
        Assert.Multiple(() =>
        {
            Assert.That(CountPrototype(setting.Prototype!), Is.EqualTo(2));
            Assert.That(charges.GetCurrentCharges(ToServer(eastRcd)), Is.EqualTo(initialCharges - setting.Cost));
        });
    }

    private async Task SetRcdProto(NetEntity rcd, ProtoId<RCDPrototype> protoId)
    {
        var previousTarget = Target;
        Target = rcd;

        await UseInHand();
        await RunTicks(3);
        Assert.That(IsUiOpen(RcdUiKey.Key), Is.True);
        await SendBui(RcdUiKey.Key, new RCDSystemMessage(protoId));
        await CloseBui(RcdUiKey.Key);
        Assert.That(IsUiOpen(RcdUiKey.Key), Is.False);

        Target = previousTarget;
    }

    private async Task SetCharges(NetEntity rcd, int value)
    {
        var charges = SEntMan.System<SharedChargesSystem>();
        await Server.WaitPost(() => charges.SetCharges(ToServer(rcd), value));
    }

    private int CountPrototype(string prototype)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var metadata))
        {
            if (!metadata.Deleted && metadata.EntityPrototype?.ID == prototype)
                count++;
        }

        return count;
    }
}
