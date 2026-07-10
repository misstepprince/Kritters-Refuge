using Content.Shared._Kritters.BloodTypes.Prototypes;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._Kritters.BloodTypes;

public sealed class KrittersBloodCompatibilitySystem : EntitySystem
{
    [Dependency] private readonly Content.Shared._Kritters.BloodTypes.KrittersBloodTypeSystem _bloodTypes = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    [ValidatePrototypeId<TagPrototype>]
    private const string ActiveTag = KrittersServerBloodTypeSystem.ActiveTag;

    private const string CellularDamageType = "Cellular";
    private const float CellularDamagePerUnit = 0.5f;

    public void ApplyTransfusionMistreatment(EntityUid recipient, Solution solution, EntityUid? origin = null)
    {
        if (!_bloodTypes.Enabled
            || !_tags.HasTag(recipient, ActiveTag)
            || !TryGetIncompatibleBloodVolume(recipient, solution, out var volume))
        {
            return;
        }

        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                [CellularDamageType] = FixedPoint2.New((float) volume * CellularDamagePerUnit),
            },
        };

        _damageable.TryChangeDamage(recipient, damage, ignoreResistances: true, origin: origin);
    }

    private bool TryGetIncompatibleBloodVolume(EntityUid recipient, Solution solution, out FixedPoint2 volume)
    {
        volume = FixedPoint2.Zero;

        foreach (var reagent in solution.Contents)
        {
            if (!_bloodTypes.TryGetBloodTypeByReagent(reagent.Reagent.Prototype, out var donorBloodType)
                || IsCompatible(recipient, donorBloodType))
            {
                continue;
            }

            volume += reagent.Quantity;
        }

        return volume > FixedPoint2.Zero;
    }

    private bool IsCompatible(EntityUid recipient, KrittersBloodTypePrototype donorBloodType)
    {
        return donorBloodType.Tags.Any(tag => IsBloodBaseTag(tag) && _tags.HasTag(recipient, tag));
    }

    private static bool IsBloodBaseTag(ProtoId<TagPrototype> tag)
    {
        return tag.Id.StartsWith("KrittersBloodBase", StringComparison.Ordinal);
    }
}
