using Content.Server.Radio.EntitySystems;
using Content.Server.Salvage.Expeditions;
using Content.Server.Speech.Components;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._CS.Salvage;

/// <summary>
/// Broadcasts a Medical SOS on the Medical radio channel to all maps when a player carrying
/// a medical tracking implant dies on an open-contract shared expedition.
/// Does not modify any existing implant or expedition logic.
/// </summary>
public sealed partial class ExpeditionMedicalSOSSystem : EntitySystem
{
    private const string RelaySpeakerName = "Planetary Relay";
    private static readonly ProtoId<RadioChannelPrototype> MedicalChannel = "Medical";

    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedMapSystem _mapManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // Only fire on death, not on critical.
        if (args.NewMobState != MobState.Dead)
            return;

        var uid = args.Target;

        if (!TryComp<ImplantedComponent>(uid, out var implanted))
            return;

        // Must be on a salvage expedition map that is an open contract.
        var xform = Transform(uid);
        if (xform.MapUid is not { } mapUid)
            return;

        if (!TryComp<SalvageExpeditionComponent>(mapUid, out var expedition))
            return;

        if (!expedition.MissionParams.OpenContract)
            return;

        // Must carry a medical tracking implant (RattleComponent tuned to Medical channel).
        var hasMedicalImplant = false;
        foreach (var implantEnt in implanted.ImplantContainer.ContainedEntities)
        {
            if (TryComp<RattleComponent>(implantEnt, out var rattle)
                && rattle.RadioChannel == "Medical")
            {
                hasMedicalImplant = true;
                break;
            }
        }

        if (!hasMedicalImplant)
            return;

        // Build the SOS message with the dead player's coordinates.
        var coords = _transform.GetMapCoordinates(uid);
        var posText = $"({(int)coords.Position.X}, {(int)coords.Position.Y})";
        var message = Loc.GetString("salvage-expedition-medical-sos", ("position", posText));

        var medicalChannel = _prototype.Index(MedicalChannel);

        // Send from each map entity so listeners on every map receive the broadcast,
        // since the Medical channel is not long-range (map-scoped by default).
        foreach (var mapId in _mapManager.GetAllMapIds())
        {
            if (mapId == MapId.Nullspace)
                continue;

            var mapEnt = _mapSystem.GetMap(mapId);
            var hadVoiceOverride = TryComp<VoiceOverrideComponent>(mapEnt, out var existingVoiceOverride);
            var voiceOverride = EnsureComp<VoiceOverrideComponent>(mapEnt);

            var oldNameOverride = voiceOverride.NameOverride;
            var oldEnabled = voiceOverride.Enabled;

            voiceOverride.NameOverride = RelaySpeakerName;
            voiceOverride.Enabled = true;

            _radio.SendRadioMessage(mapEnt, message, medicalChannel, mapEnt, escapeMarkup: false);

            if (hadVoiceOverride && existingVoiceOverride != null)
            {
                existingVoiceOverride.NameOverride = oldNameOverride;
                existingVoiceOverride.Enabled = oldEnabled;
            }
            else
            {
                RemCompDeferred<VoiceOverrideComponent>(mapEnt);
            }
        }
    }
}
