using Content.Server.Body.Components;
using Content.Server._Kritters.BloodTypes.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Kritters.BloodTypes;
using Content.Shared._Kritters.BloodTypes.Components;
using Content.Shared._Kritters.BloodTypes.Prototypes;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._Kritters.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using System.Diagnostics.CodeAnalysis;

namespace Content.Server._Kritters.BloodTypes;

public sealed class KrittersServerBloodTypeSystem : EntitySystem
{
    [Dependency] private readonly Content.Shared._Kritters.BloodTypes.KrittersBloodTypeSystem _bloodTypes = default!;
    [Dependency] private readonly KrittersBloodMetabolismSystem _bloodMetabolism = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    [ValidatePrototypeId<TagPrototype>]
    public const string ActiveTag = "KrittersBloodTypesActive";

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<BloodstreamComponent, ComponentStartup>(OnBloodstreamStartup);
        SubscribeLocalEvent<KrittersBloodTypeOverrideComponent, ComponentStartup>(OnOverrideStartup);
        SubscribeLocalEvent<KrittersBloodTypeOverrideComponent, MapInitEvent>(OnOverrideMapInit);

        _cfg.OnValueChanged(KrittersCCVars.BloodTypesEnabled, OnBloodTypesEnabledChanged);
    }

    public override void Shutdown()
    {
        _cfg.UnsubValueChanged(KrittersCCVars.BloodTypesEnabled, OnBloodTypesEnabledChanged);
    }

    private void OnBloodTypesEnabledChanged(bool enabled)
    {
        RefreshAllBloodTypes(enabled);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        ApplyToProfile(ev.Mob, ev.Profile);
    }

    private void OnBloodstreamStartup(Entity<BloodstreamComponent> ent, ref ComponentStartup args)
    {
        ApplyInferredBloodType(ent.Owner, ent.Comp);
    }

    private void OnOverrideStartup(Entity<KrittersBloodTypeOverrideComponent> ent, ref ComponentStartup args)
    {
        ApplyOverride(ent.Owner);
    }

    private void OnOverrideMapInit(Entity<KrittersBloodTypeOverrideComponent> ent, ref MapInitEvent args)
    {
        ApplyOverride(ent.Owner);
    }

    private void ApplyOverride(EntityUid uid)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream))
            return;

        ApplyInferredBloodType(uid, bloodstream);
    }

    public void ApplyToProfile(EntityUid uid, HumanoidCharacterProfile profile)
    {
        ClearBloodTypeTags(uid);

        if (!_bloodTypes.TryResolveBloodType(profile, out var bloodType))
            return;

        EnsureComp<KrittersBloodTypeSourceComponent>(uid).BloodType = bloodType.ID;

        if (!_bloodTypes.Enabled)
            return;

        ApplyBloodType(uid, bloodType, changeBloodReagent: true, applyMetabolismProfile: true);
    }

    private void ApplyInferredBloodType(EntityUid uid, BloodstreamComponent bloodstream)
    {
        ClearBloodTypeTags(uid);

        if (!_bloodTypes.Enabled || !TryResolveForEntity(uid, bloodstream, out var bloodType))
            return;

        ApplyBloodType(uid, bloodType, changeBloodReagent: ShouldSetBloodReagent(uid), applyMetabolismProfile: false);
    }

    private bool TryResolveForEntity(
        EntityUid uid,
        BloodstreamComponent bloodstream,
        [NotNullWhen(true)] out KrittersBloodTypePrototype? bloodType)
    {
        if (TryComp<KrittersBloodTypeOverrideComponent>(uid, out var bloodOverride)
            && _proto.TryIndex(bloodOverride.BloodType, out bloodType))
        {
            return true;
        }

        if (TryComp<KrittersBloodTypeSourceComponent>(uid, out var source)
            && _proto.TryIndex(source.BloodType, out bloodType))
        {
            return true;
        }

        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
            && _bloodTypes.TryGetDefaultBloodType(humanoid.Species, out bloodType))
        {
            return true;
        }

        return _bloodTypes.TryGetBloodTypeByReagent(bloodstream.BloodReagent, out bloodType);
    }

    private void ApplyBloodType(
        EntityUid uid,
        KrittersBloodTypePrototype bloodType,
        bool changeBloodReagent,
        bool applyMetabolismProfile)
    {
        if (changeBloodReagent)
            _bloodstream.ChangeBloodReagent(uid, bloodType.BloodReagent);

        var tags = new HashSet<ProtoId<TagPrototype>>(bloodType.Tags)
        {
            ActiveTag,
        };

        _tags.AddTags(uid, tags);

        if (applyMetabolismProfile)
            ApplyMetabolismProfile(uid, tags);
    }

    private void RefreshAllBloodTypes(bool enabled)
    {
        var query = EntityQueryEnumerator<BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var bloodstream))
        {
            ClearBloodTypeTags(uid);

            if (!enabled || !TryResolveForEntity(uid, bloodstream, out var bloodType))
                continue;

            ApplyBloodType(
                uid,
                bloodType,
                changeBloodReagent: ShouldSetBloodReagent(uid),
                applyMetabolismProfile: false);
        }
    }

    private bool ShouldSetBloodReagent(EntityUid uid)
    {
        return HasComp<KrittersBloodTypeOverrideComponent>(uid)
            || HasComp<KrittersBloodTypeSourceComponent>(uid);
    }

    private void ApplyMetabolismProfile(EntityUid uid, HashSet<ProtoId<TagPrototype>> tags)
    {
        foreach (var profile in _proto.EnumeratePrototypes<KrittersBloodMetabolismProfilePrototype>())
        {
            if (!tags.Contains(profile.RequiredTag))
                continue;

            ApplyMetabolismProfile(uid, profile);
            return;
        }
    }

    private void ApplyMetabolismProfile(EntityUid uid, KrittersBloodMetabolismProfilePrototype profile)
    {
        var oldNormalTemperature = profile.NormalBodyTemperature;

        // Kritters: Cold blood metabolism retargets body temperature without replacing the vanilla temperature system.
        if (TryComp<ThermalRegulatorComponent>(uid, out var regulator))
        {
            oldNormalTemperature = regulator.NormalBodyTemperature;
            regulator.NormalBodyTemperature = profile.NormalBodyTemperature;
        }

        if (profile.SetCurrentTemperature && TryComp<TemperatureComponent>(uid, out var temperature))
            _temperature.ForceChangeTemperature(uid, profile.NormalBodyTemperature, temperature);

        if (profile.ShiftTemperatureSpeedThresholds
            && TryComp<TemperatureSpeedComponent>(uid, out var temperatureSpeed))
        {
            if (profile.TemperatureSpeedThresholds != null)
            {
                _bloodMetabolism.SetTemperatureSpeedThresholds(uid, temperatureSpeed, profile.TemperatureSpeedThresholds);
                return;
            }

            var temperatureDelta = profile.NormalBodyTemperature - oldNormalTemperature;
            _bloodMetabolism.ShiftTemperatureSpeedThresholds(uid, temperatureSpeed, temperatureDelta);
        }
    }

    private void ClearBloodTypeTags(EntityUid uid)
    {
        var tags = _bloodTypes.GetAllBloodTypeTags();
        tags.Add(ActiveTag);
        _tags.RemoveTags(uid, tags);
    }
}
