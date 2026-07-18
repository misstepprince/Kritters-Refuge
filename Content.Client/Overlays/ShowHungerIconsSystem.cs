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

        // Kritters: Species-specific needs use their configured icons without bespoke overlays.
        foreach (var needType in component.Needs.Keys)
        {
            if (_needs.TryGetStatusIconPrototype(uid, needType, component, out var iconPrototype))
                ev.StatusIcons.Add(iconPrototype);
        }
    }
}
