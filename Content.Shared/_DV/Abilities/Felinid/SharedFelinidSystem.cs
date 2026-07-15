using Content.Shared._CS.Needs;
using Content.Shared._DV.Abilities;
using Content.Shared._DV.Abilities.Felinid;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Shared._DV.Abilities.Felinid;

/// <summary>
/// Makes eating <see cref="FelinidFoodComponent"/> enable a felinids hairball action.
/// Other interactions are in the server system.
/// </summary>
public abstract partial class SharedFelinidSystem : EntitySystem
{
    [Dependency] private SharedNeedsSystem _needs = default!;
    [Dependency] private ItemCougherSystem _cougher = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FelinidFoodComponent, BeforeFullyEatenEvent>(OnMouseEaten);
    }

    private void OnMouseEaten(Entity<FelinidFoodComponent> ent, ref BeforeFullyEatenEvent args)
    {
        var user = args.User;
        if (!HasComp<FelinidComponent>(user)
            || !TryComp<NeedsComponent>(user, out var hunger))
            return;

        _needs.ModifyHunger(
            user,
            ent.Comp.BonusHunger,
            hunger);
        _cougher.EnableAction(user);
    }
}
