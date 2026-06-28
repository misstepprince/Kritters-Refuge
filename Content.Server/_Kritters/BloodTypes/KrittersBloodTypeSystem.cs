using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Kritters.BloodTypes;
using Content.Shared._Kritters.BloodTypes.Prototypes;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.BloodTypes;

public sealed class KrittersServerBloodTypeSystem : EntitySystem
{
    [Dependency] private readonly Content.Shared._Kritters.BloodTypes.KrittersBloodTypeSystem _bloodTypes = default!;
    [Dependency] private readonly KrittersBloodMetabolismSystem _bloodMetabolism = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    [ValidatePrototypeId<TagPrototype>]
    public const string ActiveTag = "KrittersBloodTypesActive";

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        ApplyToProfile(ev.Mob, ev.Profile);
    }

    public void ApplyToProfile(EntityUid uid, HumanoidCharacterProfile profile)
    {
        ClearBloodTypeTags(uid);

        if (!_bloodTypes.Enabled || !_bloodTypes.TryResolveBloodType(profile, out var bloodType))
            return;

        _bloodstream.ChangeBloodReagent(uid, bloodType.BloodReagent);

        var tags = new HashSet<ProtoId<TagPrototype>>(bloodType.Tags)
        {
            ActiveTag,
        };

        _tags.AddTags(uid, tags);

        ApplyMetabolismProfile(uid, tags);
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
