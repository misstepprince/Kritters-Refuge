using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Prototypes;
using Content.Server._CS.Helpers;
using Content.Shared.StatusIcon.Components;
using Content.Shared._CS.AphroLacedVisibility;
using Content.Shared.Chemistry.Components;

namespace Content.Server._CS.AphroLacedVisibility;

/// <summary>
/// System that shows visual feedback to any container that is injected with a aphrodisiac.
/// </summary>
public sealed partial class AphroLacedVisibilitySystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private ServerAphrodisiacChecker _helper = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AphroLacedVisibilityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AphroLacedVisibilityComponent, SolutionContainerChangedEvent>(OnSolutionChange);
    }

    public void OnMapInit(Entity<AphroLacedVisibilityComponent> entity, ref MapInitEvent args)
    {
        EnsureComp<StatusIconComponent>(entity);
        CheckForAphrodisiacs(entity);
    }

    public void OnSolutionChange(Entity<AphroLacedVisibilityComponent> entity, ref SolutionContainerChangedEvent args)
    {
        if (args.Solution != null)
            CheckForAphrodisiacs(entity, args.Solution);
        else
            CheckForAphrodisiacs(entity);
    }

    public void CheckForAphrodisiacs(Entity<AphroLacedVisibilityComponent> entity)
    {
        if (!EntityManager.HasComponent<SolutionContainerManagerComponent>(entity))
            return;

        if (_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.Solution, out _, out var solution))
        {
            CheckForAphrodisiacs(entity, solution);
        }
    }

    // Override to skip solution TryGet.
    private void CheckForAphrodisiacs(Entity<AphroLacedVisibilityComponent> entity, Solution solution)
    {
        var laced = _helper.CheckForAphrodisiacs(_prototypeManager, solution);
        entity.Comp.Laced = laced;
    }
}
