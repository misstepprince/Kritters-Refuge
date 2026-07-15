using Content.Shared._CS.Needs;
using Content.Shared.Hands.Components;
using Content.Shared.Nutrition.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Returns true if the active hand entity has the specified components.
/// </summary>
public sealed partial class ThirstyPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField(required: true)]
    public NeedThreshold MinThirstState = NeedThreshold.Satisfied;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(
                NPCBlackboard.Owner,
                out var owner,
                _entManager))
        {
            return false;
        }

        if (!_entManager.TryGetComponent<NeedsComponent>(owner, out var needy))
        {
            return false;
        }

        var needs = _entManager.System<SharedNeedsSystem>();
        if (!needs.UsesThirst(owner, needy))
        {
            return false;
        }

        return needs.ThirstIsBelowThreshold(
            owner,
            MinThirstState,
            needy);
    }
}
