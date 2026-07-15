using System.Linq;
using Content.Server.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Body.Systems
{
    public sealed partial class StomachSystem : EntitySystem
    {
        [Dependency] private IGameTiming _gameTiming = default!;
        [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

        public const string DefaultSolutionName = "stomach";

        public override void Initialize()
        {
            SubscribeLocalEvent<StomachComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<StomachComponent, EntityUnpausedEvent>(OnUnpaused);
            SubscribeLocalEvent<StomachComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
        }

        private void OnMapInit(Entity<StomachComponent> ent, ref MapInitEvent args)
        {
            ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.UpdateInterval;
        }

        private void OnUnpaused(Entity<StomachComponent> ent, ref EntityUnpausedEvent args)
        {
            ent.Comp.NextUpdate += args.PausedTime;
        }

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<StomachComponent, OrganComponent, SolutionContainerManagerComponent>();
            while (query.MoveNext(
                       out var uid,
                       out var stomach,
                       out var organ,
                       out var sol))
            {
                if (_gameTiming.CurTime < stomach.NextUpdate)
                    continue;

                stomach.NextUpdate += stomach.UpdateInterval;

                // Get our solutions
                if (!_solutionContainerSystem.ResolveSolution(
                        (uid, sol),
                        DefaultSolutionName,
                        ref stomach.Solution,
                        out var stomachSolution))
                    continue;

                if (organ.Body is not { } body
                    || !_solutionContainerSystem.TryGetSolution(
                        body,
                        stomach.BodySolutionName,
                        out var bodySolution))
                    continue;

                var transferSolution = new Solution();

                var queue = new RemQueue<StomachComponent.ReagentDelta>();
                UpdateTummyGurglies(stomach, stomachSolution);
                foreach (var delta in stomach.ReagentDeltas)
                {
                    delta.Increment(stomach.UpdateInterval);
                    if (delta.Lifetime > stomach.DigestionDelay)
                    {
                        if (stomachSolution.TryGetReagent(delta.ReagentQuantity.Reagent, out var reagent))
                        {
                            if (reagent.Quantity > delta.ReagentQuantity.Quantity)
                                reagent = new(reagent.Reagent, delta.ReagentQuantity.Quantity);

                            stomachSolution.RemoveReagent(reagent);
                            transferSolution.AddReagent(reagent);
                        }

                        queue.Add(delta);
                    }
                }

                foreach (var item in queue)
                {
                    stomach.ReagentDeltas.Remove(item);
                }

                _solutionContainerSystem.UpdateChemicals(stomach.Solution.Value);

                // Transfer everything to the body solution!
                _solutionContainerSystem.TryAddSolution(bodySolution.Value, transferSolution);
            }
        }

        private void OnApplyMetabolicMultiplier(
            Entity<StomachComponent> ent,
            ref ApplyMetabolicMultiplierEvent args)
        {
            if (args.Apply)
            {
                ent.Comp.UpdateInterval *= args.Multiplier;
                return;
            }

            // This way we don't have to worry about it breaking if the stasis bed component is destroyed
            ent.Comp.UpdateInterval /= args.Multiplier;
        }

        public bool CanTransferSolution(
            EntityUid uid,
            Solution solution,
            StomachComponent? stomach = null,
            SolutionContainerManagerComponent? solutions = null)
        {
            return Resolve(uid, ref stomach, ref solutions, logMissing: false)
                && _solutionContainerSystem.ResolveSolution((uid, solutions), DefaultSolutionName, ref stomach.Solution, out var stomachSolution)
                // TODO: For now no partial transfers. Potentially change by design
                && stomachSolution.CanAddSolution(solution);
        }

        public bool TryTransferSolution(
            EntityUid uid,
            Solution solution,
            StomachComponent? stomach = null,
            SolutionContainerManagerComponent? solutions = null)
        {
            if (!Resolve(uid, ref stomach, ref solutions, logMissing: false)
                || !_solutionContainerSystem.ResolveSolution((uid, solutions), DefaultSolutionName, ref stomach.Solution)
                || !CanTransferSolution(uid, solution, stomach, solutions))
            {
                return false;
            }

            _solutionContainerSystem.TryAddSolution(stomach.Solution.Value, solution);
            // Add each reagent to ReagentDeltas. Used to track how long each reagent has been in the stomach
            foreach (var reagent in solution.Contents)
            {
                stomach.ReagentDeltas.Add(new StomachComponent.ReagentDelta(reagent));
            }

            return true;
        }

        /// <summary>
        /// Sometimes, when something reacts in your stomach to make a new reagent,
        /// It doesnt update the stomach ReagentDeltas to account for the new reagent.
        /// This goes through all the reagents in the stomach solution and if any are missing
        /// from the ReagentDeltas, it adds them with a lifetime of whatever is appropriate.
        /// to keep the yummy rumbly in your tummy accurate.
        /// </summary>
        /// <param name="stomach"></param>
        /// <param name="stomachSolution"></param>
        private static void UpdateTummyGurglies(
            StomachComponent stomach,
            Solution stomachSolution)
        {
            foreach (ReagentQuantity reagent in
                     from reagent in stomachSolution.Contents
                     let found = stomach.ReagentDeltas.Any(delta => delta.ReagentQuantity.Reagent == reagent.Reagent)
                     where !found
                     select reagent) // idfk how linq works, but it seems to work, so eat me
            {
                // New reagent!
                stomach.ReagentDeltas.Add(new StomachComponent.ReagentDelta(reagent));
            }
        }
    }
}
