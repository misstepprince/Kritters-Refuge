using Content.Shared._Kritters.BloodTypes.Prototypes;
using Content.Shared._Kritters.CCVar;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.BloodTypes;

public sealed partial class KrittersBloodTypeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly List<KrittersBloodTypePrototype> _bloodTypes = new();
    private readonly Dictionary<ProtoId<ReagentPrototype>, KrittersBloodTypePrototype> _bloodTypesByReagent = new();
    private readonly Dictionary<ProtoId<SpeciesPrototype>, List<KrittersBloodTypePrototype>> _selectableBySpecies = new();
    private readonly HashSet<ProtoId<TagPrototype>> _allBloodTypeTags = new();

    public bool Enabled => _cfg.GetCVar(KrittersCCVars.BloodTypesEnabled);

    public override void Initialize()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        RebuildPrototypeCache();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        RebuildPrototypeCache();
    }

    private void RebuildPrototypeCache()
    {
        _bloodTypes.Clear();
        _bloodTypesByReagent.Clear();
        _selectableBySpecies.Clear();
        _allBloodTypeTags.Clear();

        foreach (var bloodType in _proto.EnumeratePrototypes<KrittersBloodTypePrototype>())
        {
            _bloodTypes.Add(bloodType);
            _bloodTypesByReagent.TryAdd(bloodType.BloodReagent, bloodType);

            foreach (var tag in bloodType.Tags)
            {
                _allBloodTypeTags.Add(tag);
            }
        }
    }

    public IEnumerable<KrittersBloodTypePrototype> GetSelectableBloodTypes(ProtoId<SpeciesPrototype> species)
    {
        if (_selectableBySpecies.TryGetValue(species, out var cached))
            return cached;

        var selectable = new List<KrittersBloodTypePrototype>();
        foreach (var bloodType in _bloodTypes)
        {
            if (IsCompatibleWithSpecies(bloodType, species))
                selectable.Add(bloodType);
        }

        _selectableBySpecies[species] = selectable;
        return selectable;
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
        foreach (var type in GetSelectableBloodTypes(species))
        {
            if (type.BloodReagent != reagent)
                continue;

            bloodType = type;
            return true;
        }

        bloodType = default!;
        return false;
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

    public IReadOnlyCollection<ProtoId<TagPrototype>> GetAllBloodTypeTags()
    {
        return _allBloodTypeTags;
    }

    public bool TryGetBloodTypeByReagent(ProtoId<ReagentPrototype> reagent, out KrittersBloodTypePrototype bloodType)
    {
        return _bloodTypesByReagent.TryGetValue(reagent, out bloodType!);
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
