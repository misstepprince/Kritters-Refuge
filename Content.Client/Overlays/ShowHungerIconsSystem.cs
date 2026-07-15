using Content.Shared._CS.Needs;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Overlays;
using Content.Shared.StatusIcon.Components;

namespace Content.Client.Overlays;

public sealed partial class ShowHungerIconsSystem : EquipmentHudSystem<ShowHungerIconsComponent>
{
    [Dependency] private SharedNeedsSystem _needs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NeedsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);
    }

    private void OnGetStatusIconsEvent(EntityUid uid, NeedsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_needs.TryGetHungerStatusIconPrototype(
                uid,
                out var iconPrototype,
                component))
            ev.StatusIcons.Add(iconPrototype);
        if (_needs.TryGetThirstStatusIconPrototype(
                uid,
                out var thirstIconPrototype,
                component))
            ev.StatusIcons.Add(thirstIconPrototype);
    }
}
