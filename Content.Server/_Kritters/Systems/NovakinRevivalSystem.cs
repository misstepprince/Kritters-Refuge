using System.Diagnostics.CodeAnalysis;
using Content.Server.Body.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared._Kritters;
using Content.Shared._Kritters.Components;
using Content.Shared.Damage;
using Content.Shared._CS.Needs;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
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
    private const string NovakinCorePrototype = "OrganNovakinCore";

    [Dependency] private BodySystem _body = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tools = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<NovakinPhysiologyComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NovakinDormantCoreComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<NovakinDormantCoreComponent, NovakinCoreRestartFinishedEvent>(OnRestartFinished);
        SubscribeLocalEvent<NovakinDormantCoreComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnMobStateChanged(Entity<NovakinPhysiologyComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
        {
            CleanupDormancy(entity);
            return;
        }

        var dormant = EnsureComp<NovakinDormantCoreComponent>(entity);
        if (HasComp<UnrevivableComponent>(entity))
            return;

        EnsureComp<UnrevivableComponent>(entity).ReasonMessage = "novakin-core-dormant";
        dormant.OwnsUnrevivable = true;
    }

    private void OnInteractUsing(Entity<NovakinDormantCoreComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<NovakinPhysiologyComponent>(entity, out var physiology))
            return;

        // Only welders participate in the dormant-core restart interaction.
        // Other tools, including medical topicals and health analyzers, must continue
        // through their normal interaction systems while the shell is being repaired.
        if (!TryComp<WelderComponent>(args.Used, out var welder))
            return;

        if (!welder.Enabled)
        {
            _popup.PopupEntity(Loc.GetString("novakin-core-restart-welder"), entity, args.User);
            args.Handled = true;
            return;
        }

        if (TryGetUnrelatedUnrevivable(entity, out var unrevivable))
        {
            _popup.PopupEntity(Loc.GetString(unrevivable.ReasonMessage), entity, args.User);
            args.Handled = true;
            return;
        }

        var readiness = GetRestartReadiness(entity, physiology);
        if (!readiness.Ready)
        {
            _popup.PopupEntity(Loc.GetString("novakin-core-restart-requirements",
                ("shell", readiness.PhysicalDamage),
                ("total", readiness.TotalDamage),
                ("gas", readiness.ReservePercent),
                ("fuel", readiness.FuelPercent)), entity, args.User);
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
            || !GetRestartReadiness(entity, physiology).Ready
            || !_mobState.IsDead(entity) || TryGetUnrelatedUnrevivable(entity, out _)
            || !HasNovakinCore(entity) || !HasShellRepair(entity)
            || !IsBelowDeadThreshold(entity))
            return;

        if (args.Used is not { } used || !TryComp<WelderComponent>(used, out var welder) || !welder.Enabled)
            return;

        CleanupDormancy(entity);
        // Use the normal damage thresholds for the resulting Alive or Critical state, then restore the death lock.
        _mobThreshold.SetAllowRevives(entity, true);
        _mobThreshold.SetAllowRevives(entity, false);
        OpenReturnToBody(entity);
        args.Handled = true;
    }

    private RestartReadiness GetRestartReadiness(EntityUid uid, NovakinPhysiologyComponent physiology)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable)
            || !TryComp<NeedsComponent>(uid, out var needs)
            || !needs.Needs.TryGetValue(NeedType.Fuel, out var fuel))
            return default;

        var physical = GetBruteDamage(damageable);
        var reservePercent = physiology.MaxReserve <= 0f ? 0f : physiology.CurrentReserve / physiology.MaxReserve * 100f;
        var fuelPercent = fuel.MaxValue <= 0f ? 0f : fuel.CurrentValue / fuel.MaxValue * 100f;
        return new RestartReadiness(physical < 100f && damageable.TotalDamage.Float() < 200f && reservePercent >= 50f && fuelPercent >= 10f,
            physical, damageable.TotalDamage.Float(), reservePercent, fuelPercent);
    }

    private static float GetBruteDamage(DamageableComponent damageable)
        => damageable.Damage.DamageDict.GetValueOrDefault("Blunt").Float()
           + damageable.Damage.DamageDict.GetValueOrDefault("Slash").Float()
           + damageable.Damage.DamageDict.GetValueOrDefault("Piercing").Float();

    private readonly record struct RestartReadiness(bool Ready, float PhysicalDamage, float TotalDamage, float ReservePercent, float FuelPercent);

    private void OnRejuvenate(Entity<NovakinDormantCoreComponent> entity, ref RejuvenateEvent args)
    {
        CleanupDormancy(entity);
    }

    private void CleanupDormancy(EntityUid uid)
    {
        if (!TryComp<NovakinDormantCoreComponent>(uid, out var dormant))
            return;

        if (dormant.OwnsUnrevivable)
            RemComp<UnrevivableComponent>(uid);

        RemComp<NovakinDormantCoreComponent>(uid);
    }

    private bool TryGetUnrelatedUnrevivable(Entity<NovakinDormantCoreComponent> entity,
        [NotNullWhen(true)] out UnrevivableComponent? unrevivable)
    {
        if (!entity.Comp.OwnsUnrevivable && TryComp(entity, out unrevivable))
            return true;

        unrevivable = null;
        return false;
    }

    private bool HasNovakinCore(EntityUid uid)
    {
        foreach (var organ in _body.GetBodyOrgans(uid))
        {
            if (MetaData(organ.Id).EntityPrototype?.ID == NovakinCorePrototype)
                return true;
        }

        return false;
    }

    private bool IsBelowDeadThreshold(EntityUid uid)
        => _mobThreshold.TryGetThresholdForState(uid, MobState.Dead, out var threshold)
           && TryComp<DamageableComponent>(uid, out var damageable)
           && damageable.TotalDamage < threshold;

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
