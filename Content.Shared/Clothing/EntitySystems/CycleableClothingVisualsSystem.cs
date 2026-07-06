using Content.Shared.Clothing.Components;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.Clothing.EntitySystems;

public sealed class CycleableClothingVisualsSystem : EntitySystem
{
    [Dependency] private readonly ClothingSystem _clothing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CycleableClothingVisualsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CycleableClothingVisualsComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
    }

    private void OnMapInit(Entity<CycleableClothingVisualsComponent> ent, ref MapInitEvent args)
    {
        SetState(ent.Owner, ent.Comp, ent.Comp.CurrentState);
    }

    private void OnGetAltVerbs(Entity<CycleableClothingVisualsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.States.Count <= 1)
            return;

        var verb = new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.VerbText),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Act = () => CycleState(ent.Owner, ent.Comp)
        };

        args.Verbs.Add(verb);
    }

    private void CycleState(EntityUid uid, CycleableClothingVisualsComponent component)
    {
        SetState(uid, component, component.CurrentState + 1);
    }

    private void SetState(EntityUid uid, CycleableClothingVisualsComponent component, int index)
    {
        if (component.States.Count == 0)
            return;

        var state = MathHelper.Mod(index, component.States.Count);
        component.CurrentState = state;
        Dirty(uid, component);

        if (TryComp<ClothingComponent>(uid, out var clothing))
            _clothing.SetEquippedState(uid, component.States[state], clothing);
    }
}
