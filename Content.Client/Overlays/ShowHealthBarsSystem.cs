using Content.Shared.Inventory.Events;
using Content.Shared.Overlays;
using Content.Client._Kritters.Overlays;
using Robust.Client.Graphics;
using System.Linq;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

/// <summary>
/// Adds a health bar overlay.
/// </summary>
public sealed class ShowHealthBarsSystem : EquipmentHudSystem<ShowHealthBarsComponent>
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private EntityHealthBarOverlay _overlay = default!;
    // Kritters: optional Novakin overlay; standard health-bar behavior remains unchanged by default.
    private NovakinIntegrityOverlay _novakinIntegrityOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowHealthBarsComponent, AfterAutoHandleStateEvent>(OnHandleState);

        _overlay = new(EntityManager, _prototype);
        _novakinIntegrityOverlay = new(EntityManager);
    }

    private void OnHandleState(Entity<ShowHealthBarsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOverlay();
    }

    protected override void UpdateInternal(RefreshEquipmentHudEvent<ShowHealthBarsComponent> component)
    {
        base.UpdateInternal(component);

        // Kritters: only medical configurations opt in to the Novakin integrity overlay.
        var showNovakinIntegrity = false;

        foreach (var comp in component.Components)
        {
            foreach (var damageContainerId in comp.DamageContainers)
            {
                _overlay.DamageContainers.Add(damageContainerId);
            }

            _overlay.StatusIcon = comp.HealthStatusIcon;
            showNovakinIntegrity |= comp.ShowNovakinIntegrity;
        }

        if (!_overlayMan.HasOverlay<EntityHealthBarOverlay>())
        {
            _overlayMan.AddOverlay(_overlay);
        }

        if (showNovakinIntegrity && !_overlayMan.HasOverlay<NovakinIntegrityOverlay>())
            _overlayMan.AddOverlay(_novakinIntegrityOverlay);
        else if (!showNovakinIntegrity)
            _overlayMan.RemoveOverlay(_novakinIntegrityOverlay);
    }

    protected override void DeactivateInternal()
    {
        base.DeactivateInternal();

        _overlay.DamageContainers.Clear();
        _overlayMan.RemoveOverlay(_overlay);
        _overlayMan.RemoveOverlay(_novakinIntegrityOverlay);
    }
}
