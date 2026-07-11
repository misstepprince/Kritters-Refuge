using Content.Shared.Inventory.Events;
using Content.Shared.Clothing.Components;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared._Kritters.Novakin.Overlay;

public sealed partial class ClothesVisionSystem : EntitySystem
{
    [Dependency] private readonly ISerializationManager _serialization = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothesKrittersNightVisionComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<ClothesKrittersNightVisionComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(EntityUid uid, ClothesKrittersNightVisionComponent component, GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing)
            || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        if (!HasComp<KrittersNightVisionComponent>(args.Equipee))
        {
            var nightvision = EnsureComp<KrittersNightVisionComponent>(args.Equipee);
            nightvision.Clothes = true;
        }
    }

    private void OnUnequipped(EntityUid uid, ClothesKrittersNightVisionComponent component, GotUnequippedEvent args)
    {
        if (TryComp<KrittersNightVisionComponent>(args.Equipee, out var nightvision) && !nightvision.Clothes)
        {
            nightvision.Clothes = false;
            return;
        }

        RemComp<KrittersNightVisionComponent>(args.Equipee);
    }
}
