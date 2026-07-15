
using Content.Server.Consent;
using Content.Shared.Body.Components;
using Content.Shared.Consent;
using Content.Shared.HeightAdjust;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.Server.Body.Systems;

public sealed partial class SizeManipulationSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ConsentSystem _consent = default!;

    private static readonly ProtoId<ConsentTogglePrototype> SizeManipulationConsent = "SizeManipulation";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SizeAffectedComponent, GetSizeModifierEvent>(OnGetSizeModifier);
        SubscribeLocalEvent<SizeAffectedComponent, ComponentStartup>(OnComponentStartup);
    }

    /// <summary>
    /// When a SizeAffectedComponent is added or initialized (e.g., on client connect),
    /// trigger a size recalculation to ensure visual state is correct
    /// </summary>
    private void OnComponentStartup(EntityUid uid, SizeAffectedComponent component, ComponentStartup args)
    {
        // Only recalculate if the entity has a non-default scale
        if (Math.Abs(component.ScaleMultiplier - 1.0f) > 0.01f)
        {
            var recalcEvent = new RequestSizeRecalcEvent();
            RaiseLocalEvent(uid, ref recalcEvent);
        }
    }

    /// <summary>
    /// Contributes the size gun's modifier when collecting all size modifiers
    /// </summary>
    private void OnGetSizeModifier(EntityUid uid, SizeAffectedComponent component, ref GetSizeModifierEvent args)
    {
        // Only contribute if the scale has been changed from default
        if (Math.Abs(component.ScaleMultiplier - 1.0f) > 0.01f)
        {
            args.Modifiers.Add(new SizeModifier
            {
                Source = "SizeGun",
                Scale = component.ScaleMultiplier,
                Priority = 10 // Medium priority - applied after base but before temporary effects
            });
        }
    }

    /// <summary>
    /// Applies a size change to the target entity
    /// </summary>
    public bool TryChangeSize(EntityUid target, SizeManipulatorMode mode, EntityUid? user = null, bool safetyDisabled = false)
    {
        // Only allow size manipulation on mobs (living entities)
        if (!HasComp<MobStateComponent>(target))
        {
            Logger.Debug($"SizeManipulation: Target {ToPrettyString(target)} is not a mob, ignoring");
            return false;
        }

        // Check consent
        if (!_consent.HasConsent(target, SizeManipulationConsent))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("size-manipulator-consent-denied"), target, user.Value);

            Logger.Debug($"SizeManipulation: Consent denied for {ToPrettyString(target)}");
            return false;
        }

        var sizeComp = EnsureComp<SizeAffectedComponent>(target);

        Logger.Debug($"SizeManipulation: TryChangeSize called on {ToPrettyString(target)}, mode: {mode}, current scale: {sizeComp.ScaleMultiplier}, safety disabled: {safetyDisabled}");

        // If safety is disabled, double the max limit
        var maxScale = safetyDisabled ? sizeComp.MaxScale * 2.0f : sizeComp.MaxScale;

        float newScale;
        if (mode == SizeManipulatorMode.Grow)
        {
            newScale = sizeComp.ScaleMultiplier + sizeComp.ScaleChangeAmount;
            if (newScale > maxScale)
            {
                if (user != null)
                    _popup.PopupEntity(Loc.GetString("size-manipulator-max-size"), target, user.Value);
                return false;
            }
        }
        else
        {
            newScale = sizeComp.ScaleMultiplier - sizeComp.ScaleChangeAmount;
            if (newScale < sizeComp.MinScale)
            {
                if (user != null)
                    _popup.PopupEntity(Loc.GetString("size-manipulator-min-size"), target, user.Value);
                return false;
            }
        }

        // Update the component's scale multiplier
        sizeComp.ScaleMultiplier = newScale;
        Dirty(target, sizeComp);

        Logger.Debug($"SizeManipulation: Set scale multiplier to {newScale} for {ToPrettyString(target)}");

        // Request a size recalculation - this will collect all modifiers and apply the final scale
        var recalcEvent = new RequestSizeRecalcEvent();
        RaiseLocalEvent(target, ref recalcEvent);

        var message = mode == SizeManipulatorMode.Grow
            ? Loc.GetString("size-manipulator-target-grow")
            : Loc.GetString("size-manipulator-target-shrink");

        _popup.PopupEntity(message, target, PopupType.Medium);

        return true;
    }
}
