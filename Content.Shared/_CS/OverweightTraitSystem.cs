using Content.Shared._CS.Needs;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._CS;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class OverweightTraitSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _moveSys = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private SharedNeedsSystem _needsSys = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAudioSystem _soundSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private const float FatSpeedMod = 0.90f;
    private const float ChanceToCreak = 0.10f;
    private const float ChanceToCreakMessage = 0.50f;
    private TimeSpan _nextCreakTick = TimeSpan.Zero;
    private TimeSpan _creakDelay = TimeSpan.FromSeconds(5);

    public HashSet<string> CreakMsgsFat = new()
    {
        "chair-creak-full-1",
        "chair-creak-full-2",
        "chair-creak-full-3",
        "chair-creak-full-4",
        "chair-creak-full-5",
        "chair-creak-full-6",
        "chair-creak-full-7",
    };
    public HashSet<SoundSpecifier> CreakSounds = new()
    {
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-1.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-2.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-3.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-4.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-5.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-6.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-7.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-8.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-9.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-10.ogg"),
        new SoundPathSpecifier("/Audio/_CS/Creaks/creak-small-11.ogg"),
    };

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<OverweightTraitComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<OverweightTraitComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
    }

    private void OnInit(EntityUid uid, OverweightTraitComponent component, ComponentStartup args)
    {
        if (!TryComp<NeedsComponent>(uid, out var needy))
            return;

        SetNeedProtosToOverweightDefaults(uid, needy);
        MakeHeavier(uid);
    }

    private void OnRefreshMovespeed(EntityUid uid,
        OverweightTraitComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(FatSpeedMod);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_nextCreakTick > _gameTiming.CurTime)
            return;
        _nextCreakTick = _gameTiming.CurTime + _creakDelay;
        var query = EntityQueryEnumerator<OverweightTraitComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            CreakSeat(uid, component);
        }
    }

    private void SetNeedProtosToOverweightDefaults(EntityUid uid, NeedsComponent needy)
    {
        needy.NeedPrototypes.Clear();
        needy.NeedPrototypes.Add("NeedHungerChiara");
        needy.NeedPrototypes.Add("NeedThirstChiara");
        _needsSys.LoadNeeds(uid, needy);
    }

    private void MakeHeavier(EntityUid uid)
    {
        if (!TryComp<FixturesComponent>(uid, out var fixComp))
            return;
        {
            foreach (var (id, fixture) in fixComp.Fixtures)
            {
                if (!fixture.Hard)
                    continue; // This will skip the flammable fixture and any other fixture that is not supposed to contribute to mass

                var currentDensity = fixture.Density;
                var density = currentDensity * 1.75f;
                _physics.SetDensity(
                    uid,
                    id,
                    fixture,
                    density);
            }
        }

    }

    private void CreakSeat(EntityUid uid, OverweightTraitComponent component)
    {
        var now = _gameTiming.CurTime;
        if (component.NextCreak > now)
            return;
        component.NextCreak = now + component.CreakDelay;
        if (!_random.Prob(ChanceToCreak))
            return;
        // lets see if theyre buckled to something
        if (!TryComp<BuckleComponent>(uid, out var sitter))
            return;
        if (!sitter.Buckled
            || sitter.BuckledTo == null)
            return;
        var howFat = GetFatSeverity(uid, component);
        if (howFat != FatSeverity.Overfed
            && howFat != FatSeverity.Fed)
            return;
        // they are fat and buckled to something, creak the seat
        var seat = sitter.BuckledTo.Value;
        PlayCreak(uid, seat);
    }

    private void PlayCreak(
        EntityUid sitter,
        EntityUid seat)
    {
        var sound2Play = _random.Pick(CreakSounds);
        _soundSystem.PlayPvs(
            sound2Play,
            seat,
            AudioParams.Default);
        if (_random.Prob(ChanceToCreakMessage))
        {
            var msg = _random.Pick(CreakMsgsFat);
            var str = Loc.GetString(msg);
            _popup.PopupEntity(
                str,
                sitter,
                sitter);
        }
    }

    private FatSeverity GetFatSeverity(EntityUid uid, OverweightTraitComponent component)
    {
        if (!TryComp<NeedsComponent>(uid, out var needy))
            return FatSeverity.None;
        var hunger = _needsSys.GetHungerThreshold(uid);
        var thirst = _needsSys.GetThirstThreshold(uid);
        var highest = _needsSys.CompareThresholds(hunger, thirst);
        switch (highest)
        {
            case null:
            case NeedThreshold.Critical:
                return FatSeverity.Starving;
            case NeedThreshold.Low:
                return FatSeverity.Hungry;
            case NeedThreshold.Satisfied:
                return FatSeverity.Fed;
            case NeedThreshold.ExtraSatisfied:
                return FatSeverity.Overfed;
        }
        return FatSeverity.None;
    }
}

/// <summary>
/// Enumeration for how overweight someone is.
/// </summary>
public enum FatSeverity
{
    None,
    Starving,
    Hungry,
    Fed,
    Overfed
}

