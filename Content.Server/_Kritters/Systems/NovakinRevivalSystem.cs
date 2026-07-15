using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared._Kritters;
using Content.Shared._Kritters.Components;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Player;

namespace Content.Server._Kritters.Systems;

/// <summary>
/// The manual recovery path for a dead Novakin: mend its shell, refill its
/// nitrogen, then restart its core with a welder.
/// </summary>
public sealed partial class NovakinRevivalSystem : EntitySystem
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tools = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<NovakinPhysiologyComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NovakinDormantCoreComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<NovakinDormantCoreComponent, NovakinCoreRestartFinishedEvent>(OnRestartFinished);
    }

    private void OnMobStateChanged(Entity<NovakinPhysiologyComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        EnsureComp<NovakinDormantCoreComponent>(entity);
        EnsureComp<UnrevivableComponent>(entity).ReasonMessage = "novakin-core-dormant";
    }

    private void OnInteractUsing(Entity<NovakinDormantCoreComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<NovakinPhysiologyComponent>(entity, out var physiology))
            return;

        if (!HasShellRepair(entity) || physiology.CurrentReserve < physiology.MaxReserve * 0.5f)
        {
            _popup.PopupEntity(Loc.GetString(!HasShellRepair(entity)
                ? "novakin-core-restart-shell"
                : "novakin-core-restart-gas"), entity, args.User);
            args.Handled = true;
            return;
        }

        if (!TryComp<WelderComponent>(args.Used, out var welder) || !welder.Enabled)
        {
            _popup.PopupEntity(Loc.GetString("novakin-core-restart-welder"), entity, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = _tools.UseTool(args.Used, args.User, entity, 3f, "Welding", new NovakinCoreRestartFinishedEvent(), 3f);
        if (!args.Handled)
            _popup.PopupEntity(Loc.GetString("novakin-core-restart-welder"), entity, args.User);
    }

    private void OnRestartFinished(Entity<NovakinDormantCoreComponent> entity, ref NovakinCoreRestartFinishedEvent args)
    {
        if (args.Cancelled || !TryComp<NovakinPhysiologyComponent>(entity, out var physiology)
            || !HasShellRepair(entity) || physiology.CurrentReserve < physiology.MaxReserve * 0.5f)
            return;

        RemComp<UnrevivableComponent>(entity);
        RemComp<NovakinDormantCoreComponent>(entity);
        _mobState.ChangeMobState(entity, MobState.Critical);
        OpenReturnToBody(entity);
        args.Handled = true;
    }

    private bool HasShellRepair(EntityUid uid)
        => TryComp<DamageableComponent>(uid, out var damageable)
           && (!damageable.DamagePerGroup.TryGetValue("Brute", out var brute) || brute < 100);

    private void OpenReturnToBody(EntityUid uid)
    {
        if (_mind.TryGetMind(uid, out _, out var mind)
            && _players.TryGetSessionById(mind.UserId, out var session)
            && mind.CurrentEntity != uid)
            _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _players), session);
    }
}
