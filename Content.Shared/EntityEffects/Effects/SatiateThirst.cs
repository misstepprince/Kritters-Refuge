using Content.Shared._CS.Needs;
using Content.Shared._Kritters.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;
using DrunkEffect = Content.Shared.EntityEffects.Effects.Drunk;

namespace Content.Shared.EntityEffects.Effects;

/// <summary>
/// Default metabolism for drink reagents. Attempts to find a NeedsComponent on the target,
/// and to update it's thirst values.
/// </summary>
public sealed partial class SatiateThirst : EntityEffect
{
    private const float DefaultHydrationFactor = 3.0f;

    /// How much thirst is satiated each tick. Not currently tied to
    /// rate or anything.
    [DataField("factor")]
    public float HydrationFactor { get; set; } = DefaultHydrationFactor;

    /// Satiate thirst if a NeedsComponent can be found
    public override void Effect(EntityEffectBaseArgs args)
    {
        var uid = args.TargetEntity;
        if (!args.EntityManager.TryGetComponent(uid, out NeedsComponent? needy))
            return;

        var needs = args.EntityManager.System<SharedNeedsSystem>();
        // Kritters: drinks sustain Fuel-based physiology while non-intoxicating hydration also cools its Core.
        if (needs.ModifyThirst(uid, HydrationFactor, needy)
            || HydrationFactor <= 0f
            || !float.IsFinite(HydrationFactor)
            || HasExplicitFuelMetabolism(args))
        {
            return;
        }

        var amount = HydrationFactor * GetMetabolismScale(args);
        if (needs.TryModifyNeedLevel(uid, NeedType.Fuel, amount, needy)
            && !HeatsNovakinCore(args))
        {
            // Kritters: ordinary hydration cools a Novakin Core, while intoxicating fuel supplies heat separately.
            args.EntityManager.EventBus.RaiseLocalEvent(uid,
                new NovakinCoreCoolingEvent(amount));
        }
    }

    private static bool HasExplicitFuelMetabolism(EntityEffectBaseArgs args)
    {
        return args is EntityEffectReagentArgs { Reagent: { } reagent }
            && reagent.Metabolisms?.ContainsKey("Fuel") == true;
    }

    private static float GetMetabolismScale(EntityEffectBaseArgs args)
        => args is EntityEffectReagentArgs reagentArgs ? reagentArgs.Scale.Float() : 1f;

    private static bool HeatsNovakinCore(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs { Reagent: { } reagent })
            return false;

        return reagent.ReactiveEffects?.Values.Any(entry =>
                   entry.Effects.Any(effect => effect is FlammableReaction)) == true
            || reagent.Metabolisms?.Values.Any(entry =>
                   entry.Effects.Any(effect => effect is DrunkEffect)) == true;
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-satiate-thirst", ("chance", Probability), ("relative",  HydrationFactor / DefaultHydrationFactor));
}
