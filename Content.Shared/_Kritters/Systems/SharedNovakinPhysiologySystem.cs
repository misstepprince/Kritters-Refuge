using Content.Shared._Kritters.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;

namespace Content.Shared._Kritters.Systems;

public abstract partial class SharedNovakinPhysiologySystem : EntitySystem
{
    public float AddReserve(Entity<NovakinPhysiologyComponent> entity, float amount)
    {
        if (amount <= 0f)
            return 0f;

        var accepted = Math.Min(amount, entity.Comp.MaxReserve - entity.Comp.CurrentReserve);
        entity.Comp.CurrentReserve += accepted;
        Dirty(entity);
        return accepted;
    }

    public float RemoveReserve(Entity<NovakinPhysiologyComponent> entity, float amount)
    {
        if (amount <= 0f)
            return 0f;

        var removed = Math.Min(amount, entity.Comp.CurrentReserve);
        entity.Comp.CurrentReserve -= removed;
        Dirty(entity);
        return removed;
    }
}

/// <summary>Delivers a cryotube-filtered dose to a bloodless Novakin Core.</summary>
public sealed class NovakinCryoPodInjectionEvent(Solution solution) : EntityEventArgs
{
    public Solution Solution { get; } = solution;
}

/// <summary>Raised by fuel reagents so server temperature handling remains server-side.</summary>
public sealed class NovakinCoreFuelMetabolizedEvent(float heat) : EntityEventArgs
{
    public float Heat { get; } = heat;
}
