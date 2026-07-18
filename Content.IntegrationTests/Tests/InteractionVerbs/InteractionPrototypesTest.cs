#nullable enable

using System.Linq;
using Content.Client.Guidebook;
using Content.Server.Verbs;
using Content.Shared.InteractionVerbs;
using Content.Shared.InteractionVerbs.Requirements;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.InteractionVerbs;

[TestFixture]
[FixtureLifeCycle(LifeCycle.SingleInstance)]
[TestOf(typeof(InteractionVerbPrototype))]
public sealed class InteractionPrototypesTest
{
    public const string TestMobProto = "MobHuman";

    [Test]
    public async Task ValidatePrototypeContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var locMan = server.ResolveDependency<ILocalizationManager>();

        // TODO probably should test if an entity receives an abstract verb, but Iunno how
        foreach (var proto in protoMan.EnumeratePrototypes<InteractionVerbPrototype>())
        {
            Assert.That(proto.Abstract || proto.Action is not null,
                $"Non-abstract prototype {proto.ID} lacks an action!");

            ValidatePopupPrototype(protoMan, proto, proto.EffectSuccess);
            ValidatePopupPrototype(protoMan, proto, proto.EffectFailure);
            ValidatePopupPrototype(protoMan, proto, proto.EffectDelayed);

            if (proto.Abstract)
                continue;

            Assert.That(locMan.HasString($"interaction-{proto.ID}-name"),
                $"Interaction {proto.ID} lacks an exact-case name localization.");

            var selfTargetOnly = RequiresSelfTarget(proto.Requirement);
            ValidateEffectLocalization(protoMan, locMan, proto, proto.EffectSuccess,
                InteractionPopupPrototype.Prefix.Success, selfTargetOnly);
            ValidateEffectLocalization(protoMan, locMan, proto, proto.EffectFailure,
                InteractionPopupPrototype.Prefix.Fail, selfTargetOnly);
            if (proto.Delay > TimeSpan.Zero)
                ValidateEffectLocalization(protoMan, locMan, proto, proto.EffectDelayed,
                    InteractionPopupPrototype.Prefix.Delayed, selfTargetOnly);
        }


        await pair.CleanReturnAsync();
    }

    private static void ValidatePopupPrototype(IPrototypeManager protoMan, InteractionVerbPrototype interaction,
        InteractionVerbPrototype.EffectSpecifier? effect)
    {
        if (effect?.Popup is not { } popup)
            return;

        Assert.That(protoMan.HasIndex<InteractionPopupPrototype>(popup),
            $"Interaction {interaction.ID} references missing popup prototype {popup}.");
    }

    private static void ValidateEffectLocalization(IPrototypeManager protoMan, ILocalizationManager locMan,
        InteractionVerbPrototype interaction, InteractionVerbPrototype.EffectSpecifier? effect,
        InteractionPopupPrototype.Prefix prefix, bool selfTargetOnly)
    {
        if (effect?.Popup is not { } popupId
            || !protoMan.TryIndex<InteractionPopupPrototype>(popupId, out var popup))
            return;

        var locPrefix = $"interaction-{interaction.ID}-{prefix.ToString().ToLowerInvariant()}";
        RequireLocale(locMan, interaction.ID, locPrefix, popup.SelfSuffix ?? popup.OthersSuffix);

        if (!selfTargetOnly)
            RequireLocale(locMan, interaction.ID, locPrefix, popup.TargetSuffix ?? popup.OthersSuffix);

        RequireLocale(locMan, interaction.ID, locPrefix, popup.OthersSuffix);
    }

    private static void RequireLocale(ILocalizationManager locMan, string interactionId, string prefix,
        string? suffix)
    {
        if (suffix == null)
            return;

        var id = $"{prefix}-{suffix}-popup";
        Assert.That(locMan.HasString(id), $"Interaction {interactionId} lacks localization {id}.");
    }

    private static bool RequiresSelfTarget(InteractionRequirement? requirement)
        => requirement switch
        {
            SelfTargetRequirement self => !self.Inverted,
            ComplexRequirement { RequireAll: true } complex => complex.Requirements.Any(RequiresSelfTarget),
            _ => false
        };
}
