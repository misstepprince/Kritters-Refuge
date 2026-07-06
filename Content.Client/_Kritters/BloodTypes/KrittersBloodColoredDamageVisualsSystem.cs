using Content.Client._Kritters.BloodTypes.Components;
using Content.Client.Damage;
using Content.Shared._Kritters.CCVar;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._Kritters.BloodTypes;

public sealed class KrittersBloodColoredDamageVisualsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KrittersBloodColoredDamageVisualsComponent, AppearanceChangeEvent>(
            OnAppearanceChange,
            after: [typeof(DamageVisualsSystem)]);
        SubscribeLocalEvent<BloodstreamComponent, AfterAutoHandleStateEvent>(OnBloodstreamState);
    }

    private void OnAppearanceChange(Entity<KrittersBloodColoredDamageVisualsComponent> ent, ref AppearanceChangeEvent args)
    {
        ApplyWoundColors(ent);
    }

    private void OnBloodstreamState(Entity<BloodstreamComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<KrittersBloodColoredDamageVisualsComponent>(ent, out var coloredVisuals))
            return;

        ApplyWoundColors((ent.Owner, coloredVisuals));
    }

    private void ApplyWoundColors(Entity<KrittersBloodColoredDamageVisualsComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite)
            || !TryComp<DamageVisualsComponent>(ent, out var damageVisuals)
            || damageVisuals.DamageOverlayGroups == null)
        {
            return;
        }

        foreach (var group in ent.Comp.Groups)
        {
            if (!damageVisuals.DamageOverlayGroups.TryGetValue(group, out var damageSprite))
                continue;

            var color = GetDamageColor(ent.Owner, damageSprite);

            if (damageVisuals.TargetLayerMapKeys.Count > 0)
            {
                foreach (var layer in damageVisuals.TargetLayerMapKeys)
                {
                    if (_sprite.LayerMapTryGet((ent.Owner, sprite), $"{layer}{group}", out var spriteLayer, false))
                        _sprite.LayerSetColor((ent.Owner, sprite), spriteLayer, color);
                }
            }
            else if (_sprite.LayerMapTryGet((ent.Owner, sprite), $"DamageOverlay{group}", out var spriteLayer, false))
            {
                _sprite.LayerSetColor((ent.Owner, sprite), spriteLayer, color);
            }
        }
    }

    private Color GetDamageColor(EntityUid uid, DamageVisualizerSprite damageSprite)
    {
        if (_cfg.GetCVar(KrittersCCVars.BloodTypesEnabled)
            && TryComp<BloodstreamComponent>(uid, out var bloodstream)
            && _proto.TryIndex<ReagentPrototype>(bloodstream.BloodReagent, out var reagent))
        {
            return reagent.SubstanceColor;
        }

        return damageSprite.Color != null
            ? Color.FromHex(damageSprite.Color)
            : Color.White;
    }
}
