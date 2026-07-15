using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Kritters;

[Serializable, NetSerializable]
public sealed partial class NovakinCoreRestartFinishedEvent : SimpleDoAfterEvent
{
}
