using Content.Shared.Clothing.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Clothing;

public sealed class CycleableClothingVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CycleableClothingVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CycleableClothingVisualsComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
    }

    private void OnStartup(Entity<CycleableClothingVisualsComponent> ent, ref ComponentStartup args)
    {
        UpdateSpriteState(ent.Owner, ent.Comp);
    }

    private void OnAfterHandleState(Entity<CycleableClothingVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateSpriteState(ent.Owner, ent.Comp);
    }

    private void UpdateSpriteState(EntityUid uid, CycleableClothingVisualsComponent component)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) ||
            component.States.Count == 0 ||
            component.CurrentState < 0 ||
            component.CurrentState >= component.States.Count)
            return;

        _sprite.LayerSetRsiState((uid, sprite), 0, component.States[component.CurrentState]);
    }
}
