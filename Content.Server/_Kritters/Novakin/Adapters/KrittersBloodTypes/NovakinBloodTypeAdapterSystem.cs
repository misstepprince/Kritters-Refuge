using Content.Shared._Kritters.Novakin.Components;
using Content.Shared._Kritters.Novakin.Prototypes;
using Content.Server._Kritters.Novakin.Systems;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.Novakin.Adapters.KrittersBloodTypes;

public sealed class NovakinBloodTypeAdapterSystem : EntitySystem
{
    [Dependency] private readonly NovakinPhysiologySystem _physiology = default!;

    private static readonly Dictionary<string, ProtoId<NovakinGasPrototype>> GasSelections = new()
    {
        ["KrittersBloodNovakinOxygen"] = "NovakinGasOxygen",
        ["KrittersBloodNovakinNitrogen"] = "NovakinGasNitrogen",
        ["KrittersBloodNovakinNitrousOxide"] = "NovakinGasNitrousOxide",
        ["KrittersBloodNovakinAmmonia"] = "NovakinGasAmmonia",
        ["KrittersBloodNovakinCarbonDioxide"] = "NovakinGasCarbonDioxide",
        ["KrittersBloodNovakinWaterVapor"] = "NovakinGasWaterVapor",
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryComp<NovakinPhysiologyComponent>(args.Mob, out var physiology))
            return;

        var selection = args.Profile.BloodType;
        var gas = selection != null && GasSelections.TryGetValue(selection, out var selected)
            ? selected
            : NovakinPhysiologyComponent.DefaultGas;

        _physiology.SetGas((args.Mob, physiology), gas);
        physiology.CurrentReserve = physiology.MaxReserve;
        Dirty(args.Mob, physiology);
    }
}
