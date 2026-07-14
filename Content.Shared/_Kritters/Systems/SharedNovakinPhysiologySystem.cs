using Content.Shared._Kritters.Components;
using Content.Shared._Kritters.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Systems;

public abstract class SharedNovakinPhysiologySystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager Prototypes = default!;

    public bool SetGas(Entity<NovakinPhysiologyComponent> entity, ProtoId<NovakinGasPrototype> gas)
    {
        if (!Prototypes.TryIndex(gas, out var gasPrototype))
            return false;

        entity.Comp.Gas = gas;
        entity.Comp.MaxReserve = gasPrototype.MaxReserve;
        entity.Comp.CurrentReserve = Math.Clamp(entity.Comp.CurrentReserve, 0f, entity.Comp.MaxReserve);
        Dirty(entity);
        return true;
    }

    public float AddReserve(Entity<NovakinPhysiologyComponent> entity,
        ProtoId<NovakinGasPrototype> gas,
        float amount)
    {
        if (amount <= 0f || entity.Comp.Gas != gas)
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
