using Content.Shared._Floof.Examine;
using Content.Shared._CS.HornyQuirks;
using Robust.Shared.Prototypes;


namespace Content.Server._Floof.Examine;


public sealed partial class CustomExamineSystem : SharedCustomExamineSystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private const string InHeatProtoId = "hornyExamineInHeat";
    private const string InRutProtoId = "hornyExamineInRut";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SetCustomExamineMessage>(OnSetCustomExamineMessage);
    }

    private void OnSetCustomExamineMessage(SetCustomExamineMessage msg, EntitySessionEventArgs args)
    {
        var target = GetEntity(msg.Target);
        if (!CanChangeExamine(args.SenderSession, target))
            return;

        var comp = EnsureComp<CustomExamineComponent>(target);

        TrimData(ref msg.PublicData, ref msg.SubtleData);
        comp.PublicData = msg.PublicData;
        comp.SubtleData = msg.SubtleData;
        comp.InHeatEnabled = msg.InHeatEnabled;
        comp.InRutEnabled = msg.InRutEnabled;

        var hornyComp = EnsureComp<HornyExamineQuirksComponent>(target);
        UpdateShowable(hornyComp, InHeatProtoId, comp.InHeatEnabled);
        UpdateShowable(hornyComp, InRutProtoId, comp.InRutEnabled);

        Dirty(target, comp);
    }

    private void UpdateShowable(HornyExamineQuirksComponent hornyComp, string prototypeId, bool enabled)
    {
        if (!_prototypeManager.TryIndex<HornyExaminePrototype>(prototypeId, out var proto))
            return;

        if (enabled)
        {
            hornyComp.AddHornyExamineTrait(proto, _prototypeManager);
            return;
        }

        hornyComp.HornyShowables.Remove(proto.ID);
    }
}
