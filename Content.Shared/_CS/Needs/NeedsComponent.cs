using Content.Shared._CS;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.Needs;

/// <summary>
/// The needs component is a marker component for entities that have needs such as hunger, thirst, and such
/// holds a list of need 'datums' that, honestly, do most of the work. just dont call it needy
/// </summary>
[RegisterComponent]
[AutoGenerateComponentState]
[Serializable]
public sealed partial class NeedsComponent : Component
{
    /// <summary>
    /// The set of datums that this entity has for needs
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<NeedType, NeedDatum> Needs = new();

    /// <summary>
    /// Is the component initialized?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Ready = false;

    /// <summary>
    /// The shortest amount of time between need updates
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan MinUpdateTime = TimeSpan.Zero;

    /// <summary>
    /// The next time the needs should update
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextUpdateTime = TimeSpan.Zero;

    /// <summary>
    /// The actual way that prototypes load stuff into the needs dictionary
    /// Mainly cus I dont know how yaml works, so im gonna do it MY WAY
    /// </summary>
    [DataField("needs")]
    public List<ProtoId<NeedPrototype>> NeedPrototypes = new()
    {
        "NeedHungerDefault",
        "NeedThirstDefault",
    };

    /// <summary>
    /// Dictionary of which needs are visible, and to whom they are visible.
    /// </summary>
    [DataField]
    public Dictionary<NeedType, NeedExamineVisibility> VisibleNeeds = new()
    {
        { NeedType.Hunger, NeedExamineVisibility.Owner },
        { NeedType.Thirst, NeedExamineVisibility.Owner },
        // Kritters: Fuel follows the same owner-only readout as ordinary biological needs.
        { NeedType.Fuel, NeedExamineVisibility.Owner },
    };

    /// <summary>
    /// Is set to the current global need multiplier for debug purposes
    /// </summary>
    public int DebugCurrentNeedMultiplier = 1;

    /// <summary>
    /// COOL DEBUG STUFF
    /// Cus its impossible to edit the actual need datums through viewvariables
    /// So these vars are a workaround! They'll hold the values of the needs for easy editing
    /// And when editing is done, the values will be pushed back into the datums
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float DebugHunger
    {
        get => GetNeedCurrent(NeedType.Hunger) ?? float.MinValue;
        set => SetNeedCurrent(NeedType.Hunger, value);
    }
    [ViewVariables(VVAccess.ReadWrite)]
    public float DebugThirst
    {
        get => GetNeedCurrent(NeedType.Thirst) ?? float.MinValue;
        set => SetNeedCurrent(NeedType.Thirst, value);
    }

    /// <summary>
    /// Debug readouts of each need's current threshold data
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<string, string> DebugThresholds
    {
        get => GetNeedsThresholds();
    }

    private Dictionary<string, string> GetNeedsThresholds()
    {
        var dict = new Dictionary<string, string>();
        foreach (var (needType, datum) in this.Needs)
        {
            datum.OutputDebugInfo(ref dict);
        }
        return dict;
    }

    private float? GetNeedCurrent(NeedType type)
    {
        if (Needs.TryGetValue(type, out var datum))
        {
            return datum.CurrentValue;
        }
        return null;
    }

    private void SetNeedCurrent(NeedType type, float value)
    {
        if (Needs.TryGetValue(type, out var datum))
        {
            datum.SetCurrentValue(value);
        }
    }
}
/// <summary>
/// Visibility settings for a need on examine.
/// </summary>
public enum NeedExamineVisibility : byte
{
    /// <summary>
    /// The need is not shown on examine.
    /// </summary>
    None = 0,
    /// <summary>
    /// The need is shown on examine to everyone.
    /// </summary>
    All = 1,
    /// <summary>
    /// The need is shown on examine only to the owner.
    /// Can be overridden by ghosts, admins, and people with certain event responses.
    /// </summary>
    Owner = 2,
}
