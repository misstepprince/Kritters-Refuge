using Content.Shared._CS.RolePlayIncentiveShared;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._CS;

/// <summary>
/// A RPI Continuous Action Proxy Datum.
/// This is used to track continuous actions that should give paywards over time.
/// </summary>
[DataDefinition, Serializable]
public sealed partial class RpiContinuousActionProxyDatum(ProtoId<RpiContinuousProxyActionPrototype> proto)
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<RpiContinuousProxyActionPrototype> Proto = proto;
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan TotalAccumulated = TimeSpan.Zero;
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan LastAccumulated = TimeSpan.Zero;
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsActive = true;
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public FixedPoint2 CurrentMultiplier = 1.0f;

    private IGameTiming? _gameTiming;
    private IPrototypeManager? _prototypeManager;

    /// <summary>
    /// Call this every tick to accumulate time.
    /// </summary>
    private void Accumulate(float mult = 1.0f)
    {
        if (!IsActive)
        {
            return;
        }

        _gameTiming ??= IoCManager.Resolve<IGameTiming>();
        var delta = _gameTiming.CurTime - LastAccumulated;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;
        delta *= mult;
        TotalAccumulated += delta;
        LastAccumulated = _gameTiming.CurTime;
        GetCurrentMultiplier();
    }

    /// <summary>
    /// Call this every tick the player is in range of the proxy target.
    /// Handles all the fancy toggling and accumulation and stuff.
    /// </summary>
    public void TickInRange(float mult = 1.0f)
    {
        if (!IsActive)
        {
            SetActive();
            return;
        }
        Accumulate(mult);
    }

    /// <summary>
    /// Call this every tick the player is out of range of the proxy target.
    /// Handles all the fancy toggling and accumulation and stuff.
    /// </summary>
    public void TickOutOfRange()
    {
        SetInactive();
    }

    public void Reset()
    {
        TotalAccumulated = TimeSpan.Zero;
        LastAccumulated = TimeSpan.Zero;
        CurrentMultiplier = 1.0f;
        IsActive = false;
    }

    private void SetActive()
    {
        if (IsActive)
            return;
        IsActive = true;
        _gameTiming ??= IoCManager.Resolve<IGameTiming>();
        LastAccumulated = _gameTiming.CurTime;
    }

    private void SetInactive()
    {
        if (!IsActive)
            return;
        IsActive = false;
        LastAccumulated = TimeSpan.Zero;
    }

    public FixedPoint2 GetCurrentMultiplier()
    {
        _prototypeManager ??= IoCManager.Resolve<IPrototypeManager>();
        if (!_prototypeManager.TryIndex(Proto, out var proto))
        {
            CurrentMultiplier = FixedPoint2.New(1.0f);
            return CurrentMultiplier;
        }
        var maxTime = TimeSpan.FromMinutes(proto.MinutesToMaxBonus);
        var curTime = TotalAccumulated;
        if (curTime > maxTime)
        {
            CurrentMultiplier = FixedPoint2.New(proto.MaxMultiplier);
            return CurrentMultiplier;
        }
        var mult = curTime.TotalSeconds / maxTime.TotalSeconds;
        mult = Math.Clamp(mult * proto.MaxMultiplier, 1.0f, proto.MaxMultiplier);
        CurrentMultiplier = FixedPoint2.New(mult);
        return CurrentMultiplier;
    }
}
