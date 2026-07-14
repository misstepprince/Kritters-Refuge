using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._CS.SniffAndSmell;

/// <summary>
/// This defines a discrete scent that can be detected.
/// </summary>
[DataDefinition]
public sealed partial class Scent(
    ProtoId<ScentPrototype> scentProto,
    string scentGuid)
{
    /// <summary>
    /// The proto for this scent
    /// </summary>
    [DataField]
    public ProtoId<ScentPrototype> ScentProto = scentProto;

    /// <summary>
    /// The unique-ish ID for this scent instance
    /// </summary>
    [DataField]
    public string ScentInstanceId = scentGuid;
}
