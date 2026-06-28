using Content.Shared._Kritters.BloodTypes.Prototypes;
using Content.Shared._Kritters.CCVar;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Shared._Kritters.BloodTypes;

public sealed class KrittersBloodTypeSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public bool Enabled => _cfg.GetCVar(KrittersCCVars.BloodTypesEnabled);

    public IEnumerable<KrittersBloodTypePrototype> GetSelectableBloodTypes(ProtoId<SpeciesPrototype> species)
    {
        return _proto.EnumeratePrototypes<KrittersBloodTypePrototype>()
            .Where(type => IsCompatibleWithSpecies(type, species));
    }

    public bool TryResolveBloodType(HumanoidCharacterProfile profile, out KrittersBloodTypePrototype bloodType)
    {
        if (!string.IsNullOrWhiteSpace(profile.BloodType)
            && _proto.TryIndex<KrittersBloodTypePrototype>(profile.BloodType, out var selected)
            && IsCompatibleWithSpecies(selected, profile.Species))
        {
            bloodType = selected;
            return true;
        }

        return TryGetDefaultBloodType(profile.Species, out bloodType);
    }

    public bool IsValidOverride(ProtoId<SpeciesPrototype> species, string? bloodType)
    {
        return string.IsNullOrWhiteSpace(bloodType)
            || _proto.TryIndex<KrittersBloodTypePrototype>(bloodType, out var proto)
            && IsCompatibleWithSpecies(proto, species);
    }

    public bool TryGetDefaultBloodType(ProtoId<SpeciesPrototype> species, out KrittersBloodTypePrototype bloodType)
    {
        var reagent = GetSpeciesBloodReagent(species);
        bloodType = GetSelectableBloodTypes(species)
            .FirstOrDefault(type => type.BloodReagent == reagent)!;
        return bloodType != null;
    }

    public ProtoId<ReagentPrototype> GetSpeciesBloodReagent(ProtoId<SpeciesPrototype> species)
    {
        if (!_proto.TryIndex(species, out var speciesProto)
            || !_proto.TryIndex(speciesProto.Prototype, out var entProto)
            || !entProto.TryGetComponent<BloodstreamComponent>(out var bloodstream, EntityManager.ComponentFactory))
        {
            return "Blood";
        }

        return bloodstream.BloodReagent;
    }

    public HashSet<ProtoId<TagPrototype>> GetAllBloodTypeTags()
    {
        return _proto.EnumeratePrototypes<KrittersBloodTypePrototype>()
            .SelectMany(type => type.Tags)
            .ToHashSet();
    }

    public bool TryGetBloodTypeByReagent(ProtoId<ReagentPrototype> reagent, out KrittersBloodTypePrototype bloodType)
    {
        bloodType = _proto.EnumeratePrototypes<KrittersBloodTypePrototype>()
            .FirstOrDefault(type => type.BloodReagent == reagent)!;
        return bloodType != null;
    }

    /// <summary>
    /// Kritters: Resolves display data for blood chemistry scanners from blood type/reagent data.
    /// </summary>
    public bool TryGetScannerDisplay(
        ProtoId<ReagentPrototype> reagentId,
        out string name,
        out Color color)
    {
        if (!_proto.TryIndex<ReagentPrototype>(reagentId, out var reagent))
        {
            name = reagentId;
            color = Color.White;
            return false;
        }

        name = reagent.LocalizedName;
        color = reagent.SubstanceColor;

        if (Enabled
            && TryGetBloodTypeByReagent(reagentId, out var bloodType)
            && !string.IsNullOrWhiteSpace(bloodType.ScannerName))
            name = Loc.GetString(bloodType.ScannerName);

        return true;
    }

    private static bool IsCompatibleWithSpecies(
        KrittersBloodTypePrototype bloodType,
        ProtoId<SpeciesPrototype> species)
    {
        return bloodType.Species.Count == 0 || bloodType.Species.Contains(species);
    }
}
