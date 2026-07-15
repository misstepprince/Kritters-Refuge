using System.Linq;
using Content.Server.Botany.Components;
using Content.Server.Materials.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Audio;

namespace Content.Server.Materials;

public sealed partial class ProduceMaterialExtractorSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private MaterialStorageSystem _materialStorage = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ProduceMaterialExtractorComponent, AfterInteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ProduceMaterialExtractorComponent, FeedProduceEvent>(OnFeedProduce);
    }

    private void OnInteractUsing(Entity<ProduceMaterialExtractorComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!args.CanReach)
            return;

        args.Handled = EatTheProduce(ent, args.Used);
    }

    private void OnFeedProduce(Entity<ProduceMaterialExtractorComponent> ent, ref FeedProduceEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = EatTheProduce(ent, args.Used);
    }

    private bool EatTheProduce(Entity<ProduceMaterialExtractorComponent> ent, EntityUid used)
    {
        if (!this.IsPowered(ent, EntityManager))
            return false;

        if (!TryComp<ProduceComponent>(used, out var produce))
            return false;

        if (!_solutionContainer.TryGetSolution(
                used,
                produce.SolutionName,
                out var solution))
            return false;

        // Can produce even have fractional amounts? Does it matter if they do?
        // Questions man was never meant to answer.
        var matAmount = solution.Value.Comp.Solution.Contents
            .Where(r => ent.Comp.ExtractionReagents.Contains(r.Reagent.Prototype))
            .Sum(r => r.Quantity.Float());
        _materialStorage.TryChangeMaterialAmount(
            ent,
            ent.Comp.ExtractedMaterial,
            (int)matAmount);

        _audio.PlayPvs(ent.Comp.ExtractSound, ent);
        QueueDel(used);
        return true;
    }
}

