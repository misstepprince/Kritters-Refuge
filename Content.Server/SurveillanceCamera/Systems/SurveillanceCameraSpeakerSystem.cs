using Content.Server.Chat.Systems;
using Content.Server.Speech;
using Content.Shared.Speech;
using Content.Shared.Chat;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.SurveillanceCamera;

/// <summary>
///     This handles speech for surveillance camera monitors.
/// </summary>
public sealed class SurveillanceCameraSpeakerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SpeechSoundSystem _speechSound = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private const string EntertainmentFrequencyId = "SurveillanceCameraEntertainment";

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<SurveillanceCameraSpeakerComponent, SurveillanceCameraSpeechSendEvent>(OnSpeechSent);
    }

    private void OnSpeechSent(EntityUid uid, SurveillanceCameraSpeakerComponent component,
        SurveillanceCameraSpeechSendEvent args)
    {
        if (!component.SpeechEnabled)
        {
            return;
        }

        // If restricted to entertainment cameras, verify the monitor's active camera is on that subnet.
        if (component.RequiresEntertainmentCamera)
        {
            if (!TryComp<SurveillanceCameraMonitorComponent>(uid, out var monitor)
                || monitor.ActiveCamera == null
                || !TryComp<DeviceNetworkComponent>(monitor.ActiveCamera.Value, out var devNet)
                || devNet.ReceiveFrequencyId != EntertainmentFrequencyId)
            {
                return;
            }
        }

        var time = _gameTiming.CurTime;
        var cd = TimeSpan.FromSeconds(component.SpeechSoundCooldown);

        // this part's mostly copied from speech
        //     what is wrong with you?
        if (time - component.LastSoundPlayed < cd
            && TryComp<SpeechComponent>(args.Speaker, out var speech))
        {
            var sound = _speechSound.GetSpeechSound((args.Speaker, speech), args.Message);
            _audioSystem.PlayPvs(sound, uid);

            component.LastSoundPlayed = time;
        }

        var nameEv = new TransformSpeakerNameEvent(args.Speaker, Name(args.Speaker));
        RaiseLocalEvent(args.Speaker, nameEv);

        var name = Loc.GetString("speech-name-relay", ("speaker", Name(uid)),
            ("originalName", nameEv.VoiceName));

        // Frontier: Do not send TV messages to admins that are out of range. (GhostRangeLimit>GhostRangeLimitNoAdminCheck)
        // log to chat so people can identity the speaker/source, but avoid clogging ghost chat if there are many radios
        _chatSystem.TrySendInGameICMessage(uid, args.Message, InGameICChatType.Speak, ChatTransmitRange.GhostRangeLimitNoAdminCheck, nameOverride: name);
    }
}
