using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;

namespace Content.Shared._Kritters.Novakin.Overlay;

/// <summary>
/// Adds Novakin-style night vision to the wearer of marked clothing and removes
/// it when unequipped, while preserving night vision that is innate to the wearer.
/// </summary>
public sealed class ClothesVisionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothesKrittersNightVisionComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<ClothesKrittersNightVisionComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(
        Entity<ClothesKrittersNightVisionComponent> clothing,
        ref GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(clothing, out var wearable)
            || !wearable.Slots.HasFlag(args.SlotFlags)
            || HasComp<KrittersNightVisionComponent>(args.Equipee))
        {
            return;
        }

        var vision = EnsureComp<KrittersNightVisionComponent>(args.Equipee);
        vision.Clothes = true;
    }

    private void OnUnequipped(
        Entity<ClothesKrittersNightVisionComponent> clothing,
        ref GotUnequippedEvent args)
    {
        if (!TryComp<KrittersNightVisionComponent>(args.Equipee, out var vision)
            || !vision.Clothes)
        {
            return;
        }

        RemComp<KrittersNightVisionComponent>(args.Equipee);
    }
}
