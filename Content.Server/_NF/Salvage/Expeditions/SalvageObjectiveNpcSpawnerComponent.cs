using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._NF.Salvage.Expeditions;

/// <summary>
/// Periodically spawns faction-themed NPCs from a salvage objective structure while
/// enforcing a local population cap.
/// </summary>
[RegisterComponent]
[AutoGenerateComponentPause]
public sealed partial class SalvageObjectiveNpcSpawnerComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> SpawnPrototypes = new();

    [DataField(required: true)]
    public HashSet<ProtoId<NpcFactionPrototype>> NearbyFactions = new();

    [DataField]
    public float SpawnIntervalSeconds = 76f;

    // 30x30 tile area is approximated by a 15-tile radius.
    [DataField]
    public float NearbyRange = 15f;

    [DataField]
    public int MaxNearby = 5;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSpawn = TimeSpan.Zero;
}
