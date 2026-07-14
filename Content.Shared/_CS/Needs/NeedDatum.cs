using System.Diagnostics.CodeAnalysis;
using Content.Shared._CS.RolePlayIncentiveShared;
using Content.Shared.Alert;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.Needs;

/// <summary>
/// A datum that holds information about a specific need
/// Also holds the logic for decaying the need over time
/// And some other stuff
/// Starts life blank, and needs to be filled out by the NeedsComponent
/// And it fills itself out using the NeedPrototype~
/// </summary>
[DataDefinition, Serializable]
public sealed partial class NeedDatum
{
    /// <summary>
    /// The type of need this datum represents
    /// </summary>
    public NeedType NeedType = NeedType.Hunger;

    /// <summary>
    /// The prototype ID of the need this datum represents
    /// </summary>
    public ProtoId<NeedPrototype> PrototypeId = default!;

    /// <summary>
    /// The name of the need
    /// </summary>
    public string NeedName = "Busty Vixens";

    /// <summary>
    /// Color associated with this need, for text and icons
    /// </summary>
    public Color NeedColor = Color.White;

    /// <summary>
    /// The current value of the need
    /// </summary>
    [DataField("currentValue")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float CurrentValue = 100.0f;

    /// <summary>
    /// The maximum value of the need
    /// </summary>
    [DataField("maxValue")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MaxValue = 100.0f;

    /// <summary>
    /// The minimum value of the need
    /// </summary>
    [DataField("minValue")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MinValue = 0.0f;

    /// <summary>
    /// The rate at which the need decays over time (per second)
    /// </summary>
    [DataField("decayRate")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float DecayRate = 0.0f;

    /// <summary>
    /// The thresholds for this need
    /// </summary>
    [DataField("thresholds")]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<NeedThreshold, float> Thresholds = new();

    /// <summary>
    /// The alerts for this need
    /// </summary>
    [DataField("alerts")]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<NeedThreshold, ProtoId<AlertPrototype>?> Alerts = new();

    /// <summary>
    /// The hud icon... things for this need
    /// </summary>
    [DataField("statusIcons")]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<NeedThreshold, string> StatusIcons = new();

    /// <summary>
    /// The slowdown modifiers for this need
    /// </summary>
    [DataField("slowdownModifiers")]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<NeedThreshold, float> SlowdownModifiers = new();

    /// <summary>
    /// The RPI modifiers for this need
    /// </summary>
    [DataField("rpiModifiers")]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<NeedThreshold, float> RpiModifiers = new();

    /// <summary>
    /// The current threshold that the need is in
    /// </summary>
    public NeedThreshold CurrentThreshold = NeedThreshold.Satisfied;

    /// <summary>
    /// The Alert Category associated with this need, if any.
    /// </summary>
    public ProtoId<AlertCategoryPrototype> AlertCategory;

    /// <summary>
    /// Rate it updates in seconds.
    /// </summary>
    [DataField("updateRateSeconds")]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan UpdateRateSeconds = TimeSpan.FromSeconds(1f);

    /// <summary>
    /// Next update time.
    /// </summary>
    [DataField("nextUpdateTime")]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextUpdateTime = TimeSpan.Zero;

    /// <summary>
    /// Next update time.
    /// </summary>
    public Dictionary<NeedThreshold, NeedSlowDebuff?> DebuffSlows = new();

    public bool MovementUpdated = false;

    public float StopDigestingAtThisPoint = -1f;

    #region Constructor
    /// <summary>
    /// Constructor for the NeedDatum
    /// Takes in a NeedPrototype and fills out the datum
    /// </summary>
    public NeedDatum(NeedPrototype proto)
    {
        if (proto is not { ID: not null or not "" })
            throw new ArgumentException("NeedPrototype must have a valid ID");
        PrototypeId = proto.ID ?? throw new ArgumentException("NeedPrototype must have a valid ID");
        NeedName = proto.NeedName; // dont call me needy
        if (Enum.TryParse<NeedType>(proto.NeedKind, out var needType))
        {
            NeedType = needType;
        }
        else
        {
            throw new ArgumentException($"Invalid NeedType in NeedPrototype: {proto.NeedKind}");
        }

        var hazCoolor = Color.TryFromName(proto.NeedColor, out var doColor);
        NeedColor = hazCoolor ? doColor : Color.White;
        MaxValue = proto.MaxValue;
        MinValue = proto.MinValue;
        AlertCategory = proto.AlertCategory;
        UpdateRateSeconds = TimeSpan.FromSeconds(proto.UpdateRateSeconds);
        CalcualteDecayRate(proto);
        CalcualteInitialValue(proto);
        FillOutThresholds(proto);
        FillOutAlerts(proto);
        FillOutStatusIcons(proto);
        FillOutSlowdownModifiers(proto);
        FillOutRpiModifiers(proto);
        FillOutDebuffSlows(proto);
        UpdateCurrentThreshold();
        UpdateStopDigestingPoint();
    }

    /// <summary>
    /// Takes in the time in minutes it should take to go from max to min, and calculates the decay rate
    /// In units per second
    /// </summary>
    private void CalcualteDecayRate(NeedPrototype proto)
    {
        var proMinutes = proto.MinutesFromMaxToMin * proto.TimeScalar;
        if (proMinutes <= 0)
        {
            DecayRate = 1.0f; // Default decay rate if invalid value is provided
            throw new ArgumentException("MinutesFromMaxToMin must be greater than 0");
        }

        DecayRate = MaxValue / (float)(proMinutes * 60.0);
    }

    /// <summary>
    /// Takes in the starting time in minutes and calculates the initial value of the need
    /// Basically, we start at max, and decay for the starting time
    /// </summary>
    private void CalcualteInitialValue(NeedPrototype proto)
    {
        if (proto.StartingMinutesWorthOfDecay < 0)
        {
            CurrentValue = MaxValue;
            return;
        }

        CurrentValue = MaxValue - (DecayRate * (float)(proto.StartingMinutesWorthOfDecay * 60.0 * proto.TimeScalar));
    }

    /// <summary>
    /// Creates the thresholds dictionary from the prototype
    /// </summary>
    private void FillOutThresholds(NeedPrototype proto)
    {
        Thresholds[NeedThreshold.ExtraSatisfied] = proto.ExtraSatisfiedMinutesFromFull;
        Thresholds[NeedThreshold.Satisfied] = proto.SatisfiedMinutesFromFull;
        Thresholds[NeedThreshold.Low] = proto.LowMinutesFromFull;
        Thresholds[NeedThreshold.Critical] = float.MaxValue; // ensure its the lowest threshold
        // Convert minutes to actual values
        foreach (var key in Thresholds.Keys)
        {
            Thresholds[key] = MaxValue - (DecayRate * (Thresholds[key] * 60.0f) * proto.TimeScalar);
            Thresholds[key] = Math.Clamp(
                Thresholds[key],
                MinValue,
                MaxValue);
        }
    }

    /// <summary>
    /// Adds the alerts from the prototype to the datum, filling in nulls where necessary
    /// </summary>
    private void FillOutAlerts(NeedPrototype proto)
    {
        Alerts[NeedThreshold.ExtraSatisfied] = proto.ExtraSatisfiedAlert;
        Alerts[NeedThreshold.Satisfied] = proto.SatisfiedAlert;
        Alerts[NeedThreshold.Low] = proto.LowAlert;
        Alerts[NeedThreshold.Critical] = proto.CriticalAlert;
    }

    /// <summary>
    /// Adds the status icons from the prototype to the datum, filling in empty strings where necessary
    /// </summary>
    private void FillOutStatusIcons(NeedPrototype proto)
    {
        StatusIcons[NeedThreshold.ExtraSatisfied] = proto.ExtraSatisfiedIcon ?? string.Empty;
        StatusIcons[NeedThreshold.Satisfied] = proto.SatisfiedIcon ?? string.Empty;
        StatusIcons[NeedThreshold.Low] = proto.LowIcon ?? string.Empty;
        StatusIcons[NeedThreshold.Critical] = proto.CriticalIcon ?? string.Empty;
    }

    /// <summary>
    /// Adds the slowdown modifiers from the prototype to the datum, filling in 1.0s where necessary
    /// </summary>
    private void FillOutSlowdownModifiers(NeedPrototype proto)
    {
        SlowdownModifiers[NeedThreshold.ExtraSatisfied] = proto.ExtraSatisfiedSlowdown;
        SlowdownModifiers[NeedThreshold.Satisfied] = proto.SatisfiedSlowdown;
        SlowdownModifiers[NeedThreshold.Low] = proto.LowSlowdown;
        SlowdownModifiers[NeedThreshold.Critical] = proto.CriticalSlowdown;
        // clamp to 'reasonable' values
        foreach (var key in SlowdownModifiers.Keys)
        {
            SlowdownModifiers[key] = Math.Clamp(
                SlowdownModifiers[key],
                0.05f,
                10.0f);
        }
    }

    /// <summary>
    /// Adds the RPI modifiers from the prototype to the datum, filling in 1.0s where necessary
    /// </summary>
    private void FillOutRpiModifiers(NeedPrototype proto)
    {
        RpiModifiers[NeedThreshold.ExtraSatisfied] = proto.ExtraSatisfiedRoleplayIncentive;
        RpiModifiers[NeedThreshold.Satisfied] = proto.SatisfiedRoleplayIncentive;
        RpiModifiers[NeedThreshold.Low] = proto.LowRoleplayIncentive;
        RpiModifiers[NeedThreshold.Critical] = proto.CriticalRoleplayIncentive;
        // clamp to 'reasonable' values
        foreach (var key in RpiModifiers.Keys)
        {
            RpiModifiers[key] = Math.Clamp(
                RpiModifiers[key],
                0.05f,
                10.0f);
        }
    }

    /// <summary>
    /// Fills out the DebuffSlows dictionary from the prototype
    /// </summary>
    private void FillOutDebuffSlows(NeedPrototype proto)
    {
        DebuffSlows.Clear();
        DebuffSlows[NeedThreshold.ExtraSatisfied] = null;
        DebuffSlows[NeedThreshold.Satisfied] = null;
        DebuffSlows[NeedThreshold.Low] = null;
        DebuffSlows[NeedThreshold.Critical] = null;
        if (proto.ExtraSatisfiedDebuffSlowdown != null)
        {
            DebuffSlows[NeedThreshold.ExtraSatisfied] = new NeedSlowDebuff(proto.ExtraSatisfiedDebuffSlowdown.Value);
        }
        if (proto.SatisfiedDebuffSlowdown != null)
        {
            DebuffSlows[NeedThreshold.Satisfied] = new NeedSlowDebuff(proto.SatisfiedDebuffSlowdown.Value);
        }
        if (proto.LowDebuffSlowdown != null)
        {
            DebuffSlows[NeedThreshold.Low] = new NeedSlowDebuff(proto.LowDebuffSlowdown.Value);
        }
        if (proto.CriticalDebuffSlowdown != null)
        {
            DebuffSlows[NeedThreshold.Critical] = new NeedSlowDebuff(proto.CriticalDebuffSlowdown.Value);
        }
    }

    /// <summary>
    /// Sets the point at which digestion should stop if tyhey are asleeped
    /// this will be 80% of the way between Satisfied and Low
    /// </summary>
    private void UpdateStopDigestingPoint()
    {
        var satisfiedValue = GetValueForThreshold(NeedThreshold.Satisfied);
        var lowValue = GetValueForThreshold(NeedThreshold.Low);
        StopDigestingAtThisPoint = satisfiedValue - ((satisfiedValue - lowValue) * 0.8f);
    }

    #endregion

    #region Need Value Manipulation
    /// <summary>
    /// Decays the need over time
    /// </summary>
    /// <param name="deltaTime">The time since the last update (in seconds)</param>
    /// <param name="sleeping">Whether the entity is currently sleeping</param>
    public void Decay(float deltaTime, bool sleeping)
    {
        if (sleeping)
        {
            if (CurrentValue < StopDigestingAtThisPoint)
            {
                return; // dont decay if we're already low or worse
            }
        }
        CurrentValue -= DecayRate * deltaTime;
        CurrentValue = Math.Clamp(
            CurrentValue,
            MinValue,
            MaxValue);
    }

    /// <summary>
    /// Ticks the debuff slows, if any
    /// </summary>
    public void TickDebuffSlows(TimeSpan curTime)
    {
        if (DebuffSlows.TryGetValue(GetCurrentThreshold(), out var debuff)
            && debuff != null)
        {
            debuff.TickSlowdown(curTime);
        }
    }

    /// <summary>
    /// Modifies the current value of the need by a specified amount
    /// </summary>
    public void ModifyCurrentValue(float amount)
    {
        CurrentValue += amount;
        CurrentValue = Math.Clamp(
            CurrentValue,
            MinValue,
            MaxValue);
    }

    /// <summary>
    /// Sets the current value of the need to a specified amount
    /// </summary>
    public void SetCurrentValue(float amount)
    {
        CurrentValue = Math.Clamp(
            amount,
            MinValue,
            MaxValue);
    }
    #endregion

    #region Threshold Stuff
    public NeedThreshold GetCurrentThreshold()
    {
        return GetThresholdForValue(CurrentValue);
    }

    /// <summary>
    /// Gets the current threshold of the need based on its current value
    /// Its the threshold with the highest minimum value that is less than or equal to the current value
    /// </summary>
    public NeedThresholdUpdateResult UpdateCurrentThreshold()
    {
        var oldThreshold = CurrentThreshold;
        var current = GetThresholdForValue(CurrentValue);
        CurrentThreshold = current;
        var currThrupdresu = new NeedThresholdUpdateResult(oldThreshold, current);
        if (currThrupdresu.Changed)
            MovementUpdated = true;
        return currThrupdresu;
    }

    public NeedThreshold GetThresholdForValue(float value)
    {
        var outThresh = NeedThreshold.Critical; // Start at the lowest threshold
        var highestMinValue = float.MinValue;
        foreach (var (threshold, minValue) in Thresholds)
        {
            if (value >= minValue)
            {
                if (minValue > highestMinValue)
                {
                    highestMinValue = minValue;
                    outThresh = threshold;
                }
            }
        }

        return outThresh;
    }

    public float GetValueForThreshold(NeedThreshold threshold)
    {
        Thresholds.TryGetValue(threshold, out var value);
        return value;
    }

    public bool IsBelowThreshold(NeedThreshold threshold)
    {
        var threshValue = Thresholds[threshold];
        return CurrentValue < threshValue;
    }

    public bool HasQueuedMoveUpdate()
    {
        if (MovementUpdated)
        {
            return true;
        }
        if (DebuffSlows.TryGetValue(GetCurrentThreshold(), out var debuff)
            && debuff != null
            && debuff.Updated)
        {
            debuff.Updated = false;
            return true;
        }
        return false;
    }
    #endregion

    #region Apply Effects
    /// <summary>
    /// Modifies the RPI event multiplier based on the current threshold
    /// </summary>
    public void ModifyRpiEvent(ref GetRpiModifier ev)
    {
        if (RpiModifiers.TryGetValue(GetCurrentThreshold(), out var modifier))
        {
            ev.Modify(modifier, 0.0f);
        }
    }

    /// <summary>
    /// Modifies the movement speed based on the current threshold
    /// </summary>
    public void ApplyMovementSpeedModifier(ref RefreshMovementSpeedModifiersEvent args)
    {
        if (SlowdownModifiers.TryGetValue(GetCurrentThreshold(), out var modifier))
        {
            args.ModifySpeed(modifier, modifier);
            if (DebuffSlows.TryGetValue(GetCurrentThreshold(), out var debuff)
                && debuff != null)
            {
                var speedMult = debuff.GetSlow();
                args.ModifySpeed(speedMult, speedMult);
                MovementUpdated = false;
            }
        }
    }

    /// <summary>
    /// Gets all queued debuff slow messages for this need
    /// </summary>
    public List<string> GetDebuffSlowMessages()
    {
        var messages = new List<string>();
        if (DebuffSlows.TryGetValue(GetCurrentThreshold(), out var debuff)
            && debuff != null
            && !string.IsNullOrEmpty(debuff.QueuedMessage))
        {
            messages.Add(debuff.GetAndClearQueuedMessage());
        }
        return messages;
    }
    #endregion

    #region Get - Status Icon, Alert
    /// <summary>
    /// Gets the StatusIcon for the current threshold, if any
    /// </summary>
    public bool GetCurrentStatusIcon([NotNullWhen(true)] out string? icon)
    {
        if (StatusIcons.TryGetValue(GetCurrentThreshold(), out var iconId)
            && !string.IsNullOrEmpty(iconId))
        {
            icon = iconId;
            return true;
        }

        icon = null;
        return false;
    }

    public ProtoId<AlertPrototype> GetCurrentAlert()
    {
        if (!Alerts.TryGetValue(GetCurrentThreshold(), out var alert))
        {
            return default;
        }

        return alert ?? default;
    }
    #endregion

    #region Time Stuff
    /// <summary>
    /// Outputs a TimeSpan relating to how long it should take to go from ValueA to ValueB, in decay time.
    /// </summary>
    public TimeSpan GetDecayTime(float valueA, float valueB)
    {
        if (DecayRate <= 0)
            return TimeSpan.MaxValue;
        var delta = Math.Abs(valueA - valueB);
        var seconds = delta / DecayRate;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Takes in a TimeSpan and returns a pretty string representing that time span
    /// Something like "2 hours, 3 minutes, and 5 seconds", or "5 minutes, and 1 second", or "1 hour, and 2 seconds"
    /// </summary>
    public string Time2String(TimeSpan timeCool, bool actualTime = false)
    {
        if (actualTime)
        {
            return timeCool.ToString(@"d\:hh\:mm\:ss");
        }
        var totMins = (int)timeCool.TotalMinutes;
        var hours = (int)timeCool.TotalHours;
        var minutes = timeCool.Minutes;
        string timeString;
        // Round to the nearest 15 minutes for anything over 10 minutes
        // under 10 minutes, just say "soon"
        if (totMins < 10)
        {
            return "[color=pink]soon[/color]!";
        }
        // okay round the minutes to the nearest 10
        minutes = (int)(Math.Round(minutes / 10.0) * 10);
        if (hours > 0)
        {
            if (minutes > 0)
            {
                timeString = $"{hours} hour{(hours == 1 ? "" : "s")}, and {minutes} minute{(minutes == 1 ? "" : "s")}";
            }
            else
            {
                timeString = $"{hours} hour{(hours == 1 ? "" : "s")}";
            }
        }
        else
        {
            timeString = $"{minutes} minute{(minutes == 1 ? "" : "s")}";
        }
        return $"[color=yellow]{timeString}[/color]";
    }


    /// <summary>
    /// Gets the Theoretical time it would take to go from CurrentValue to MinValue
    /// </summary>
    public TimeSpan GetTimeToMinValue()
    {
        return GetDecayTime(CurrentValue, MinValue);
    }

    /// <summary>
    /// Gets the Theoretical time it would take to go from CurrentValue to the next threshold
    /// </summary>
    public TimeSpan GetTimeFromNowToNextThreshold()
    {
        return GetDecayTime(CurrentValue, Thresholds[GetCurrentThreshold()]);
    }
    #endregion

    #region Examine Info
    /// <summary>
    /// Gets a pretty list of all the buffs and debuffs for this need
    /// </summary>
    public void GetBuffDebuffList(ref string stringOut)
    {
        var speed = GetSpeedModText();
        var rpi = GetRpiModText();
        if (speed == null && rpi == null)
        {
            return;
        }
        stringOut += Loc.GetString("examinable-need-effect-header") + "\n";
        if (speed != null)
        {
            stringOut += speed + "\n";
        }

        if (rpi != null)
        {
            stringOut += rpi + "\n";

        }
    }

    public string? GetSpeedModText()
    {
        if (!SlowdownModifiers.TryGetValue(GetCurrentThreshold(), out var modifier))
        {
            return null;
        }

        return GetModifierText(
            modifier,
            true,
            "Movement Speed",
            "examinable-need-effect-buff",
            "examinable-need-effect-debuff");
    }

    public string? GetRpiModText()
    {
        if (!RpiModifiers.TryGetValue(GetCurrentThreshold(), out var modifier))
        {
            return null;
        }

        return GetModifierText(
            modifier,
            true,
            "RP Incentive",
            "examinable-need-effect-buff",
            "examinable-need-effect-debuff");
    }

    /// <summary>
    /// Turns a modifier into a pretty string
    /// </summary>
    public string? GetModifierText(
        float modifier,
        bool higherIsBetter,
        string kind,
        string buffLocKey,
        string debuffLocKey)
    {
        if (Math.Abs(modifier - 1.0f) < 0.001f) // floating point imprecision
            return null;
        var percent = $"{(modifier - 1.0f) * 100.0f:+0;-0}%";
        if ((modifier > 1.0f && higherIsBetter)
            || (modifier < 1.0f && !higherIsBetter))
        {
            return Loc.GetString(
                buffLocKey,
                ("kind", kind),
                ("amount", percent));
        }
        else
        {
            return Loc.GetString(
                debuffLocKey,
                ("kind", kind),
                ("amount", percent));
        }
    }
    #endregion

    #region Debugging
    /// <summary>
    /// Modifies an input dictionary to add debug information about this need
    /// </summary>
    public void OutputDebugInfo(ref Dictionary<string, string> dict)
    {
        var keyBase = NeedType.ToString();
        dict[$"{keyBase} START"] = "-----";
        dict[$"{keyBase} Current Value"] = CurrentValue.ToString("0.00");
        dict[$"{keyBase} Max Value"] = MaxValue.ToString("0.00");
        dict[$"{keyBase} Min Value"] = MinValue.ToString("0.00");
        dict[$"{keyBase} Decay Rate"] = DecayRate.ToString("0.0000") + " per second";
        dict[$"{keyBase} Current Threshold"] = GetCurrentThreshold().ToString();
        dict[$"{keyBase} Next Update In"] = $"{(NextUpdateTime - TimeSpan.Zero).TotalSeconds:0.00} seconds";
        dict[$"{keyBase} Current RPI Modifier"] = RpiModifiers[GetCurrentThreshold()].ToString("0.00") + "x";
        dict[$"{keyBase} Current Speed Modifier"] = SlowdownModifiers[GetCurrentThreshold()].ToString("0.00") + "x";
        dict[$"{keyBase} Current Alert"] = GetCurrentAlert().ToString();
        dict[$"{keyBase} Current Status Icon"] = GetCurrentStatusIcon(out var icon) ? icon : "None";
        foreach (var (threshold, value) in Thresholds)
        {
            var currCurr = threshold ==  GetCurrentThreshold();
            var isCurr = currCurr ? " <-" : " ";
            dict[$"{keyBase} Threshold {threshold} Value{isCurr}"] = value.ToString("0.00");
            if (currCurr)
            {
                // time until we bottom out in this threshold
                var timeToNext = GetDecayTime(CurrentValue, value);
                dict[$"{keyBase} Threshold {threshold} Time To Next{isCurr}"] = Time2String(timeToNext, true);
            }
            dict[$"{keyBase} Threshold {threshold} RPI Modifier{isCurr}"] =
                RpiModifiers[threshold].ToString("0.00") + "x";
            dict[$"{keyBase} Threshold {threshold} Speed Modifier{isCurr}"] =
                SlowdownModifiers[threshold].ToString("0.00") + "x";
            dict[$"{keyBase} Threshold {threshold} Alert{isCurr}"] = Alerts[threshold]?.ToString() ?? "None";
            dict[$"{keyBase} Threshold {threshold} Status Icon{isCurr}"] =
                string.IsNullOrEmpty(StatusIcons[threshold]) ? "None" : StatusIcons[threshold];
        }

        dict["Fuzzy"] = "hugged";
        dict[$"{keyBase} END"] = "-----";
    }
    #endregion
}

#region Events

    /// <summary>
    /// An event raised when something ELSE wants to mess with the examine text
    /// </summary>
    public sealed class NeedExamineInfoEvent(NeedDatum need, EntityUid examinee, bool isSelf) : EntityEventArgs
    {
        public NeedDatum Need = need;
        public EntityUid Examinee = examinee;
        public bool IsSelf = isSelf;
        public List<string> AdditionalInfoLines = new();

        public void AppendAdditionalInfoLines(ref string baseString)
        {
            foreach (var line in AdditionalInfoLines)
            {
                baseString += line + "\n";
            }
        }

        public void AddPercentBuff(string kind, string text, float modifier)
        {
            if (Math.Abs(modifier - 1.0f) < 0.001f)
                return;
            var percent = $"{(modifier - 1.0f) * 100.0f:+0;-0}%";
            if (modifier < 1.0f)
            {
                AdditionalInfoLines.Add(
                    Loc.GetString(
                        "examinable-need-effect-buff",
                        ("kind", kind),
                        ("amount", percent)));
            }
            else
            {
                AdditionalInfoLines.Add(
                    Loc.GetString(
                        "examinable-need-effect-debuff",
                        ("kind", kind),
                        ("amount", percent)));
            }
        }

        public void AddRawBuff(string kind, string text, bool isBuff)
        {
            if (isBuff)
            {
                AdditionalInfoLines.Add(
                    Loc.GetString(
                        "examinable-need-effect-buff-custom",
                        ("kind", kind)));
            }
            else
            {
                AdditionalInfoLines.Add(
                    Loc.GetString(
                        "examinable-need-effect-debuff-custom",
                        ("kind", kind)));
            }
        }
    }

    #endregion

public struct NeedThresholdUpdateResult(NeedThreshold oldThreshold, NeedThreshold newThreshold)
{
    public NeedThreshold OldThreshold = oldThreshold;
    public NeedThreshold NewThreshold = newThreshold;
    public bool Changed => OldThreshold != NewThreshold;
}

/// <summary>
/// A data holder that'll handle the random slow debuff entitys
/// </summary>
[Serializable]
public sealed class NeedSlowDebuff()
{
    public ProtoId<NeedSlowdownPrototype> Pid;
    public TimeSpan MaxDuration;
    public TimeSpan Cooldown;
    public TimeSpan CooldownEndsAt;
    public TimeSpan SlowedUntil;
    public TimeSpan CheckInterval;
    public TimeSpan NextCheckTime;
    public TimeSpan LastTickTime;
    public float SlowMult;
    public float ChancePerSecond;
    public bool Slowed;
    public bool Updated;
    public string StartMessage = "need-slowdown-default-start";
    public string EndMessage = "need-slowdown-default-end";
    public string QueuedMessage = string.Empty;

    public NeedSlowDebuff(ProtoId<NeedSlowdownPrototype> pid) : this()
    {
        Pid = pid;
        var protoMan = IoCManager.Resolve<IPrototypeManager>();
        if (!protoMan.TryIndex<NeedSlowdownPrototype>(pid, out var proto))
        {
            throw new ArgumentException($"Invalid NeedSlowdownPrototype prototype ID: {pid}");
        }
        MaxDuration = TimeSpan.FromSeconds(proto.DurationSeconds);
        Cooldown = TimeSpan.FromMinutes(proto.MinMinutesBetweenSlowdowns);
        CheckInterval = TimeSpan.FromSeconds(1.0);
        SlowMult = proto.SpeedModifier;
        StartMessage = proto.StartMessage;
        EndMessage = proto.EndMessage;
        ChancePerSecond = Math.Clamp(
            proto.ChancePercent / 100.0f,
            0.0f,
            1.0f);
        // initialize other fields to defaults
        InitializeWorkingValues();
    }

    private void InitializeWorkingValues()
    {
        CooldownEndsAt = TimeSpan.Zero;
        SlowedUntil = TimeSpan.Zero;
        NextCheckTime = TimeSpan.Zero;
        LastTickTime = TimeSpan.Zero;
        Slowed = false;
    }

    /// <summary>
    /// Call this every second
    /// </summary>
    public void TickSlowdown(TimeSpan curTime)
    {
        if (curTime < NextCheckTime)
            return;
        var timeSinceLastTick = (curTime - LastTickTime).TotalSeconds;
        NextCheckTime = curTime + CheckInterval;
        // there may be a large gap between checks, if they were satisfied for a while
        if (timeSinceLastTick > 10.0)
        {
            InitializeWorkingValues();
        }
        LastTickTime = curTime;
        if (Slowed)
        {
            TryEndSlowdown(curTime);
        }
        else
        {
            TryStartSlowdown(curTime, timeSinceLastTick);
        }
    }

    private void TryEndSlowdown(TimeSpan curTime)
    {
        if (!Slowed)
            return;
        if (curTime >= SlowedUntil)
        {
            Slowed = false;
            CooldownEndsAt = curTime + Cooldown;
            QueuedMessage = EndMessage;
            Updated = true;
        }
    }

    private void TryStartSlowdown(TimeSpan curTime, double timeSinceLastTick)
    {
        if (Slowed)
            return;
        if (curTime < CooldownEndsAt)
            return;
        // chance to start slowdown
        // the longer its been since the last check, the higher the chance
        var effectiveChance = 1.0 - Math.Pow(1.0 - ChancePerSecond, timeSinceLastTick);
        var roll = IoCManager.Resolve<IRobustRandom>().NextDouble();
        if (roll < effectiveChance)
        {
            Slowed = true;
            SlowedUntil = curTime + MaxDuration;
            QueuedMessage = StartMessage;
            Updated = true;
        }
    }

    public string GetAndClearQueuedMessage()
    {
        var msg = Loc.GetString(QueuedMessage);
        QueuedMessage = string.Empty;
        Updated = false;
        return msg;
    }

    public float GetSlow()
    {
        return Slowed ? SlowMult : 1.0f;
    }
}

// i like this function but it is not used
// List<string> timeParts = new();
// if (hours > 0)
//     timeParts.Add(hours == 1 ? "1 hour" : $"{hours} hours");
// if (minutes > 0)
//     timeParts.Add(minutes == 1 ? "1 minute" : $"{minutes} minutes");
// // if (seconds > 0)
// //     timeParts.Add(seconds == 1 ? "1 second" : $"{seconds} seconds");
// if (timeParts.Count == 0)
//     timeParts.Add("no time at all");
// for (var i = 0; i < timeParts.Count; i++)
// {
//     timeParts[i] = $"[color=yellow]{timeParts[i]}[/color]";
// }
// // Format into a nice string
// switch (timeParts.Count)
// {
//     case 1: // only one part
//         timeString = timeParts[0];
//         break;
//     case 2: // two parts, just join with and
//         timeString = $"{timeParts[0]} and {timeParts[1]}";
//         break;
//     default:
//     {
//         for (var i = 0; i < timeParts.Count; i++)
//         {
//             if (i == timeParts.Count - 1)
//             {
//                 timeString += $"and {timeParts[i]}";
//             }
//             else
//             {
//                 timeString += $"{timeParts[i]}, ";
//             }
//         }
//
//         break;
//     } // in ss13, this would be handled with english_list(list_of_stuff, "and")
// } // why must everything in life be so complicated

// why must I fail at every attempt at masonry
// return timeString;
// }
