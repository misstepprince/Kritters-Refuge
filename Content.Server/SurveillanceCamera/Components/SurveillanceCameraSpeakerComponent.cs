namespace Content.Server.SurveillanceCamera;

/// <summary>
///     This allows surveillance camera monitors to speak what cameras hear.
/// </summary>
[RegisterComponent]
public sealed partial class SurveillanceCameraSpeakerComponent : Component
{
    // mostly copied from Speech
    [DataField("speechEnabled")] public bool SpeechEnabled = true;

    [ViewVariables] public float SpeechSoundCooldown = 0.5f;

    public TimeSpan LastSoundPlayed = TimeSpan.Zero;

    /// <summary>
    ///     When true, speech is only relayed when the monitor's active camera is an entertainment camera
    ///     (i.e. its receive frequency is SurveillanceCameraEntertainment).
    /// </summary>
    [DataField("requiresEntertainmentCamera")]
    public bool RequiresEntertainmentCamera = false;
}
