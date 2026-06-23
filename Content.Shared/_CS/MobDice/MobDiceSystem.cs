using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._CS.MobDice;

/// <summary>
/// This handles...
/// </summary>
public sealed class MobDiceSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly IRobustRandom _rand = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _pop = default!;
    [Dependency] private readonly SharedDoAfterSystem _doafter = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MobDiceComponent, MapInitEvent>(AddMobDiceOnMapInit);
        SubscribeLocalEvent<MobDiceComponent, MobDiceRollSelfEvent>(EventRollDice);
        SubscribeLocalEvent<MobDiceComponent, RollDiceDoAfterEvent>(DoAfterComplete);
        SubscribeLocalEvent<MobDiceComponent, GetVerbsEvent<InnateVerb>>(GetRollVerb);
    }

    private void AddMobDiceOnMapInit(Entity<MobDiceComponent> entity, ref MapInitEvent args)
    {
        _actions.AddAction(
            entity.Owner,
            ref entity.Comp.RollActionEntity,
            entity.Comp.RollAction);
    }

    private void GetRollVerb(Entity<MobDiceComponent> entity, ref GetVerbsEvent<InnateVerb> args)
    {
        if (HasComp<GhostComponent>(args.User))
            return;
        if (args.Target == args.User)
            return;

        EntityUid me = args.User;
        EntityUid them = args.Target;

        InnateVerb rollVerb = new()
        {
            Text = Loc.GetString(
                "mob-dice-roll-verb-get-verbs-text",
                ("sides", entity.Comp.Sides)),
            Category = VerbCategory.Actions,
            Act = () =>
            {
                RollDice(
                    entity,
                    me,
                    them);
            },
        };

        args.Verbs.Add(rollVerb);
    }

    private void EventRollDice(Entity<MobDiceComponent> entity, ref MobDiceRollSelfEvent args)
    {
        RollDice(
            entity,
            args.Performer,
            args.Performer);
    }

    private void RollDice(Entity<MobDiceComponent> entity, EntityUid doer, EntityUid target)
    {
        if (!_net.IsServer)
            return;
        RollDiceDoAfterEvent daev = new (
            GetNetEntity(doer),
            GetNetEntity(target));
        _doafter.TryStartDoAfter(
            new DoAfterArgs(
                EntityManager,
                entity.Owner,
                entity.Comp.RollDelay,
                daev,
                doer)
            {
                BreakOnHandChange = false,
                BreakOnDropItem = false,
                BreakOnMove = true,
                BreakOnWeightlessMove = false,
                DuplicateCondition = DuplicateConditions.None,
        });
        // PlayRollStartSound(doer);
        DoNotifyRolling(doer, target);
    }

    private void DoAfterComplete(Entity<MobDiceComponent> entity, ref RollDiceDoAfterEvent args)
    {
        if (args.Cancelled)
            return;
        if (!_net.IsServer)
            return;
        EntityUid performerUid = GetEntity(args.Performer);
        EntityUid performedOnUid = GetEntity(args.PerformedOn);
        PerformRollDice(performerUid, performedOnUid);
    }

    public void PerformRollDice(EntityUid player1, EntityUid player2)
    {
        MobDiceResult? result1 = RollDice(player1, player2);
        if (result1 == null)
            return;
        MobDiceResult? result2 = player2 != player1 ? RollDice(player2, player1) : null;
        PlayRollResultSound(player1);
        ReportResult(
            player1,
            player2,
            result1.Value,
            result2);
    }

    private MobDiceResult? RollDice(EntityUid player, EntityUid? target = null)
    {
        int sides = 20;
        if (TryComp<MobDiceComponent>(player, out var comp)) {
            sides = comp.Sides;
        }

        int roll = _rand.Next(1, sides + 1);
        TimeSpan when = _time.CurTime;
        MobDiceResult result = new (
            when,
            sides,
            roll,
            player,
            target);

        if(comp is not null)
            comp.RollHistory[when] = result;

        return result;
    }

    private void DoNotifyRolling(EntityUid doer, EntityUid target)
    {
        if (!TryComp<MobDiceComponent>(doer, out var comp))
            throw new InvalidOperationException($"Tried to notify rolling mob dice for entity {ToPrettyString(doer)} which does not have a {nameof(MobDiceComponent)}.");
        EntityUid doerIdentity = Identity.Entity(doer, EntityManager);
        if (doer == target)
        {
            _pop.PopupEntity(
                Loc.GetString(
                    "mob-dice-rolling-self-popup-message",
                    ("sides", comp.Sides),
                    ("doer", doerIdentity)),
                doer,
                PopupType.Small);
        }
        else
        {
            EntityUid targetIdentity = Identity.Entity(target, EntityManager);
            _pop.PopupEntity(
                Loc.GetString(
                    "mob-dice-rolling-other-popup-message",
                    ("doer", doerIdentity),
                    ("sides", comp.Sides),
                    ("target", targetIdentity)),
                doer,
                PopupType.Small);
        }
    }

    private void PlayRollStartSound(EntityUid entity)
    {
        _audio.PlayPvs(
            new SoundCollectionSpecifier("Dice"),
            entity,
            AudioParams.Default.WithVolume(1f));
    }

    private void PlayRollResultSound(EntityUid entity)
    {
        _audio.PlayPvs(
            new SoundCollectionSpecifier("Dice"),
            entity,
            AudioParams.Default.WithVolume(1f));
    }

    private void ReportResult(
        EntityUid doer,
        EntityUid target,
        MobDiceResult result1,
        MobDiceResult? result2
        )
    {
        EntityUid doerIdentity   = Identity.Entity(doer, EntityManager);
        EntityUid targetIdentity = Identity.Entity(target, EntityManager);

        if (doer == target)
        {
            _pop.PopupEntity(
                Loc.GetString(
                    "mob-dice-roll-result-self-popup-message",
                    ("doer", doerIdentity),
                    ("result", result1.Result),
                    ("sides", result1.Sides)),
                doer,
                PopupType.Medium);
            return;
        }

        // doer rolled against target
        if (result2 == null)
            throw new InvalidOperationException("Tried to report mob dice roll results for two different entities, but second result was null.");
        MobDiceResult result2Val = result2.Value;

        // popup for doer
        _pop.PopupEntity(
            Loc.GetString(
                "mob-dice-roll-result-self-popup-message",
                ("doer", doerIdentity),
                ("result", result1.Result),
                ("sides", result1.Sides)),
            doer,
            PopupType.Medium);
        // popup for target
        _pop.PopupEntity(
            Loc.GetString(
                "mob-dice-roll-result-self-popup-message",
                ("doer", targetIdentity),
                ("result", result2Val.Result),
                ("sides", result2Val.Sides)),
            target,
            PopupType.Medium);
        // Oh no, a tie!
        if (result1.Result == result2Val.Result)
        {
            _pop.PopupEntity(
                Loc.GetString(
                    "mob-dice-roll-result-both-popup-message-tie",
                    ("result", result1.Result),
                    ("doer", doerIdentity),
                    ("target", targetIdentity)),
                doer,
                PopupType.MediumCautionLingering);
            return;
        }
        EntityUid winner = result1.Result > result2Val.Result ? doer : target;
        EntityUid loser  = winner == doer ? target : doer;
        // popup for winner
        _pop.PopupEntity(
            Loc.GetString(
                "mob-dice-roll-result-winner-popup-message",
                ("winner", Identity.Entity(winner, EntityManager)),
                ("result" , Math.Max(result1.Result, result2Val.Result))),
            winner,
            PopupType.MediumCautionLingering);
        // popup for loser
        _pop.PopupEntity(
            Loc.GetString(
                "mob-dice-roll-result-lose-popup-message",
                ("loser", Identity.Entity(loser, EntityManager)),
                ("result" , Math.Min(result1.Result, result2Val.Result))),
            loser,
            PopupType.MediumCautionLingering);
    }
}
