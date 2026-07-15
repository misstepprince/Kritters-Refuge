using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Prototypes;
using Content.Shared.StatusIcon.Components;
using Content.Shared._CS.AphroLacedVisibility;
using Content.Client.Consent;
using Content.Shared.Examine;

namespace Content.Client._CS.AphroLacedVisibility;

/// <summary>
/// System that shows visual feedback to any container that is injected with a aphrodisiac.
/// </summary>
public sealed partial class AphroLacedVisibilitySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IClientConsentManager _consentManager = default!;

    private readonly string _consentId = "AphrodisiacsVisibility";
    private bool? _consentOn = null;
    private bool ConsentOn
    {
        get // this is probably wrong xd
        {
            if (_consentOn == null)
                CheckConsent();

            if (_consentOn == null)
                return false; // failsafe

            return _consentOn.Value;
        }
        set => _consentOn = value;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AphroLacedVisibilityComponent, GetStatusIconsEvent>(OnGetStatusIcon);
        SubscribeLocalEvent<AphroLacedVisibilityComponent, ExaminedEvent>(OnExamine);
        _consentManager.OnServerDataLoaded += CheckConsent;

        CheckConsent();
    }

    private void OnExamine(EntityUid uid, AphroLacedVisibilityComponent comp, ref ExaminedEvent args)
    {
        if (!comp.Laced
        || !ConsentOn)
            return;

        args.PushMarkup(Loc.GetString("container-laced-with-aphrodisiacs"));
    }

    private void CheckConsent()
    {
        if (!_consentManager.HasLoaded)
            return;

        var settings = _consentManager.GetConsent();
        ConsentOn = settings.Toggles.TryGetValue(_consentId, out var val) && val == "on";
    }

    private void OnGetStatusIcon(EntityUid uid, AphroLacedVisibilityComponent component, ref GetStatusIconsEvent args)
    {
        if (!component.Laced
        || !ConsentOn)
            return;

        args.StatusIcons.Add(_prototypeManager.Index(component.Icon));
    }
}
