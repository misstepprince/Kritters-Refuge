using Content.Shared._Kritters.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Kritters.Systems;

public abstract partial class SharedNovakinPhysiologySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NovakinPhysiologyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
    }

    public float AddReserve(Entity<NovakinPhysiologyComponent> entity, float amount)
    {
        var current = NormalizeReserve(entity);
        if (!float.IsFinite(amount) || amount <= 0f)
            return 0f;

        var max = GetValidMaxReserve(entity.Comp);
        var accepted = Math.Min(amount, max - current);
        if (accepted > 0f)
        {
            entity.Comp.CurrentReserve = current + accepted;
            Dirty(entity);
        }

        return accepted;
    }

    public float RemoveReserve(Entity<NovakinPhysiologyComponent> entity, float amount)
    {
        var current = NormalizeReserve(entity);
        if (!float.IsFinite(amount) || amount <= 0f)
            return 0f;

        var removed = Math.Min(amount, current);
        if (removed > 0f)
        {
            entity.Comp.CurrentReserve = current - removed;
            Dirty(entity);
        }

        return removed;
    }

    protected static float GetValidMaxReserve(NovakinPhysiologyComponent component)
        => float.IsFinite(component.MaxReserve) ? Math.Max(0f, component.MaxReserve) : 0f;

    public static bool IsInResourceStasis(IEntityManager entities, EntityUid uid)
    {
        if (entities.HasComponent<InsideCryoPodComponent>(uid))
            return true;

        return entities.TryGetComponent(uid, out BuckleComponent? buckle)
            && buckle.BuckledTo is { } bed
            && entities.HasComponent<NovakinResourceStasisComponent>(bed);
    }

    private float NormalizeReserve(Entity<NovakinPhysiologyComponent> entity)
    {
        var max = GetValidMaxReserve(entity.Comp);
        var current = float.IsNaN(entity.Comp.CurrentReserve)
            ? 0f
            : Math.Clamp(entity.Comp.CurrentReserve, 0f, max);
        if (entity.Comp.CurrentReserve.Equals(current))
            return current;

        entity.Comp.CurrentReserve = current;
        Dirty(entity);
        return current;
    }

    private static void OnRefreshMovement(Entity<NovakinPhysiologyComponent> entity,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        var multiplier = entity.Comp.HeatSpeedMultiplier
            * entity.Comp.ReserveSpeedMultiplier
            * entity.Comp.ColdSpeedMultiplier;
        args.ModifySpeed(multiplier, multiplier);
    }
}

/// <summary>Delivers a cryotube-filtered dose to a bloodless Novakin Core.</summary>
public sealed class NovakinCryoPodInjectionEvent(Solution solution) : EntityEventArgs
{
    public Solution Solution { get; } = solution;
    public bool Accepted { get; set; }
}

/// <summary>Requests server-authoritative cooling without crossing the Core's cold danger threshold.</summary>
public sealed class NovakinCoreCoolingEvent(float temperatureDelta) : EntityEventArgs
{
    public float TemperatureDelta { get; } = temperatureDelta;
}
