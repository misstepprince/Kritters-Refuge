using Content.Shared._Kritters.Components;
using Content.Shared._CS.Needs;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.EntityEffects;

/// <summary>
/// Converts a metabolized fuel reagent into Core fuel for Novakin only.
/// </summary>
public sealed partial class NovakinFuel : EntityEffect
{
    [DataField]
    public float Fuel = 5f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<NovakinPhysiologyComponent>(args.TargetEntity, out _))
            return;

        var amount = args is EntityEffectReagentArgs reagentArgs
            ? Fuel * reagentArgs.Quantity.Float()
            : Fuel;
        args.EntityManager.System<SharedNeedsSystem>().TryModifyNeedLevel(args.TargetEntity, NeedType.Fuel, amount);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;
}
