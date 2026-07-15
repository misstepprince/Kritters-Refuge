using Content.Server._CS;
using Content.Shared._CS.RolePlayIncentiveShared;
using Content.Shared.Destructible;
using Content.Shared.Mining;
using Content.Shared.Mining.Components;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Mining;

/// <summary>
/// This handles creating ores when the entity is destroyed.
/// </summary>
public sealed partial class MiningSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private RoleplayIncentiveSystem _RpiSystem = default!;
    [Dependency] private IGameTiming _timing = null!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVeinComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OreVeinComponent, DestructionEventArgs>(OnDestruction);
    }

    private void OnDestruction(EntityUid uid, OreVeinComponent component, DestructionEventArgs args)
    {
        if (component.CurrentOre == null)
            return;

        // Frontier
        if (component.PreventSpawning)
            return;
        // End Frontier

        var proto = _proto.Index<OrePrototype>(component.CurrentOre);

        // if (args.Destroyer != null
        //     && TryComp<RoleplayIncentiveComponent>(args.Destroyer, out var rpi))
        // {
        //     PayoutLoser(
        //         args.Destroyer.Value,
        //         proto.Payout,
        //         rpi);
        // }

        if (proto.OreEntity == null)
            return;

        var coords = Transform(uid).Coordinates;
        var toSpawn = _random.Next(proto.MinOreYield, proto.MaxOreYield+1);
        for (var i = 0; i < toSpawn; i++)
        {
            Spawn(proto.OreEntity, coords.Offset(_random.NextVector2(0.2f)));
        }
    }

    private void OnMapInit(EntityUid uid, OreVeinComponent component, MapInitEvent args)
    {
        if (component.CurrentOre != null || component.OreRarityPrototypeId == null || !_random.Prob(component.OreChance))
            return;

        component.CurrentOre = _proto.Index<WeightedRandomOrePrototype>(component.OreRarityPrototypeId).Pick(_random);
    }

    private void PayoutLoser(EntityUid toPay, int payout, RoleplayIncentiveComponent rpi)
    {
        if (payout <= 0)
            return;

        var taxData = _RpiSystem.GetTaxBracketData(toPay);
        var finalPayout = (int) (payout * taxData.ActionMultipliers[RpiActionType.Mining]);
        if (finalPayout <= 0)
            return;
        // the paypig message
        var msg = Loc.GetString(
            "coyote-rpi-plus-fund-yellow",
            ("amount", finalPayout));
        var ev = new RpiImmediatePayEvent(
            _timing.CurTime,
            RpiActionType.Mining,
            finalPayout,
            msg,
            true);
        RaiseLocalEvent(toPay, ev); // make this loser feel like a winner
    }
}
