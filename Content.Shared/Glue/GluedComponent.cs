using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Glue;

[RegisterComponent]
[Access(typeof(SharedGlueSystem))]
public sealed partial class GluedComponent : Component
{

    [DataField("until", customTypeSerializer: typeof(Content.Shared.Serialization.ContentTimeOffsetSerializer)), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan Until;

    [DataField("duration", customTypeSerializer: typeof(Content.Shared.Serialization.ContentTimeOffsetSerializer)), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan Duration;
}
