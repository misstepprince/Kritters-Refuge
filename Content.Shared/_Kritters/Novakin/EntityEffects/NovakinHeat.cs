using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Kritters.Novakin.EntityEffects;

/// <summary>
/// Raises a Novakin's core temperature by a direct temperature delta rather than heat energy.
/// </summary>
public sealed partial class NovakinHeat : EventEntityEffect<NovakinHeat>
{
    [DataField(required: true)]
    public float TemperatureDelta;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;
}
