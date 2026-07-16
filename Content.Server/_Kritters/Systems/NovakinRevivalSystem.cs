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

        var readiness = GetRestartReadiness(entity, physiology);
        if (!readiness.Ready)
        {
            _popup.PopupEntity(Loc.GetString("novakin-core-restart-requirements",
                ("shell", NovakinDisplayFormat.Number(readiness.PhysicalDamage)),
                ("total", NovakinDisplayFormat.Number(readiness.TotalDamage)),
                ("gas", NovakinDisplayFormat.Number(readiness.ReservePercent)),
                ("fuel", NovakinDisplayFormat.Number(readiness.FuelPercent))), entity, args.User);
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
            || args.Used is not { } used || !TryComp<WelderComponent>(used, out var welder) || !welder.Enabled)
            return;

        RemComp<UnrevivableComponent>(entity);
        RemComp<NovakinDormantCoreComponent>(entity);
        _mobState.ChangeMobState(entity, MobState.Critical);
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

    private bool HasShellRepair(EntityUid uid)
        => TryComp<DamageableComponent>(uid, out var damageable)
           && damageable.TotalDamage.Float() < 200f
           && GetBruteDamage(damageable) < 100f;

    private static float GetBruteDamage(DamageableComponent damageable)
        => damageable.Damage.DamageDict.GetValueOrDefault("Blunt").Float()
           + damageable.Damage.DamageDict.GetValueOrDefault("Slash").Float()
           + damageable.Damage.DamageDict.GetValueOrDefault("Piercing").Float();

    private readonly record struct RestartReadiness(bool Ready, float PhysicalDamage, float TotalDamage, float ReservePercent, float FuelPercent);

    private void OpenReturnToBody(EntityUid uid)
    {
        if (_mind.TryGetMind(uid, out _, out var mind)
            && _players.TryGetSessionById(mind.UserId, out var session)
            && mind.CurrentEntity != uid)
            _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _players), session);
    }
}
