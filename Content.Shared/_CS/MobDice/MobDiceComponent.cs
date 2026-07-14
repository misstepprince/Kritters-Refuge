using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.MobDice;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class MobDiceComponent : Component
{
    [DataField]
    public EntProtoId RollAction = "ActionRollDice";

    [DataField]
    public EntityUid? RollActionEntity;

    [DataField]
    public int Sides = 20;

    [DataField]
    public Dictionary<TimeSpan, MobDiceResult> RollHistory = new();

    [DataField]
    public TimeSpan RollDelay = TimeSpan.FromSeconds(3);
}

/// <summary>
/// The result of a rolled dice!
/// </summary>
[DataRecord]
public partial struct MobDiceResult(
    TimeSpan whenDid,
    int sides,
    int result,
    EntityUid roller,
    EntityUid? target
)
{
    [DataField]
    public TimeSpan WhenDid = whenDid;

    [DataField]
    public int Sides = sides;

    [DataField]
    public int Result = result;

    [DataField]
    public EntityUid Roller = roller;

    [DataField]
    public EntityUid? Target = target;
}

public sealed partial class MobDiceRollSelfEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class RollDiceDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity Performer;

    [DataField]
    public NetEntity PerformedOn;

    public RollDiceDoAfterEvent(NetEntity performer, NetEntity performedOn)
    {
        Performer = performer;
        PerformedOn = performedOn;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}
