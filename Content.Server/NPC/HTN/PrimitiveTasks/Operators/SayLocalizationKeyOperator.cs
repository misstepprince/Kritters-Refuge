using Content.Server.Chat.Systems;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class SayLocalizationKeyOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    private ChatSystem _chat = default!;

    [DataField(required: true)]
    public string Key = string.Empty;

    [DataField(required: true)]
    public string LocalizationKeyPrefix = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);

        _chat = sysManager.GetEntitySystem<ChatSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<object>(Key, out var value, _entManager))
            return HTNOperatorStatus.Failed;

        var @string = value.ToString();
        if (value is float f)
            // truncate to integer
            @string = ((int) f).ToString();
        if (@string is not { })
            return HTNOperatorStatus.Failed;

        var locKey = LocalizationKeyPrefix + @string;
        @string = Loc.GetString(locKey);

        var speaker = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _chat.TrySendInGameICMessage(
            speaker,
            @string,
            InGameICChatType.Speak,
            hideChat: false,
            hideLog: false);

        return base.Update(blackboard, frameTime);
    }
}
