using System.Globalization;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Content.Shared.Serialization;

/// <summary>
/// A copy-capable version of Robust's time-offset serializer.
///
/// This remains in content so clients using the public 282.0.0 engine can load
/// content assemblies while the engine-side serializer fix is unreleased.
/// </summary>
public sealed class ContentTimeOffsetSerializer : ITypeSerializer<TimeSpan, ValueDataNode>, ITypeCopyCreator<TimeSpan>
{
    public TimeSpan Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<TimeSpan>? instanceProvider = null)
    {
        if (context is { WritingReadingPrototypes: true })
            return TimeSpan.Zero;

        if (context is not EntityDeserializer { CurrentReadingEntity.PostInit: true } ctx)
            return TimeSpan.Zero;

        var time = TimeSpan.FromSeconds(double.Parse(node.Value, CultureInfo.InvariantCulture));
        return time > TimeSpan.MaxValue - ctx.Timing.CurTime ? TimeSpan.MaxValue : time + ctx.Timing.CurTime;
    }

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        return double.TryParse(node.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, "Failed parsing TimeSpan");
    }

    public DataNode Write(ISerializationManager serializationManager, TimeSpan value,
        IDependencyCollection dependencies, bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        if (context is not EntitySerializer serializer
            || serializer.WritingReadingPrototypes
            || !serializer.EntMan.TryGetComponent(serializer.CurrentEntity, out MetaDataComponent? meta)
            || meta.EntityLifeStage < EntityLifeStage.MapInitialized)
        {
            DebugTools.Assert(value == TimeSpan.Zero || context?.WritingReadingPrototypes != true,
                "non-zero time offsets in prototypes are not supported. If required, initialize offsets on map-init");
            return new ValueDataNode("0");
        }

        var metadata = serializer.EntMan.System<MetaDataSystem>();
        value -= serializer.Timing.CurTime - metadata.GetPauseTime(serializer.CurrentEntity!.Value.Owner, meta);
        return new ValueDataNode(value.TotalSeconds.ToString(CultureInfo.InvariantCulture));
    }

    public TimeSpan CreateCopy(ISerializationManager serializationManager, TimeSpan source,
        IDependencyCollection dependencies, SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        return source;
    }
}
