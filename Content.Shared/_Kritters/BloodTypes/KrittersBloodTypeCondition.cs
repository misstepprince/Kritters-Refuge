using Content.Shared._Kritters.CCVar;
using Content.Shared.EntityEffects;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Kritters.BloodTypes;

public sealed partial class KrittersBloodType : EntityEffectCondition
{
    private const string ActiveTag = "KrittersBloodTypesActive";

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<TagPrototype>))]
    public string? RequiredTag;

    [DataField]
    public bool Invert;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        var cfg = IoCManager.Resolve<IConfigurationManager>();
        if (!cfg.GetCVar(KrittersCCVars.BloodTypesEnabled)
            || !args.EntityManager.TryGetComponent<TagComponent>(args.TargetEntity, out var tag))
        {
            return false;
        }

        var tagSystem = args.EntityManager.System<TagSystem>();
        if (!tagSystem.HasTag(tag, ActiveTag))
            return false;

        return RequiredTag == null || tagSystem.HasTag(tag, RequiredTag) ^ Invert;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString(
            "reagent-effect-condition-guidebook-kritters-blood-type",
            ("tag", RequiredTag ?? ActiveTag),
            ("invert", Invert));
    }
}
