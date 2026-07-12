using Content.Shared._Kritters.Novakin.Components;
using Content.Shared._Kritters.Novakin.Prototypes;
using Content.Server._Kritters.Novakin.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.Novakin.Adapters.KrittersBloodTypes;

public sealed class NovakinBloodTypeAdapterSystem : EntitySystem
{
    [Dependency] private readonly NovakinPhysiologySystem _physiology = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

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

        ConfigureStartingInhalers(args.Mob, gas);
    }

    /// <summary>
    /// Configures adaptive inhalers anywhere inside the spawned character's container hierarchy.
    /// Kept public so alternate character-profile adapters can reuse the starting-kit behavior.
    /// </summary>
    public void ConfigureStartingInhalers(EntityUid mob, ProtoId<NovakinGasPrototype> gas)
    {
        if (!_prototypes.TryIndex(gas, out var gasPrototype))
            return;

        var query = EntityQueryEnumerator<NovakinInhalerComponent, GasTankComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var inhaler, out var tank, out var transform))
        {
            if (!inhaler.AdaptToOwnerGas || !IsContainedBy(transform, mob))
                continue;

            inhaler.Gas = gas;
            tank.Air.Clear();
            tank.Air.SetMoles(gasPrototype.Gas, inhaler.MaxMoles);
            Dirty(uid, tank);
        }
    }

    private bool IsContainedBy(TransformComponent transform, EntityUid owner)
    {
        var parent = transform.ParentUid;
        while (parent != EntityUid.Invalid)
        {
            if (parent == owner)
                return true;

            if (!TryComp<TransformComponent>(parent, out var parentTransform))
                return false;

            parent = parentTransform.ParentUid;
        }

        return false;
    }
}
