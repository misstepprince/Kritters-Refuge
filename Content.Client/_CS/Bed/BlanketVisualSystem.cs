using Content.Shared._CS.Bed.Components;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._CS.Bed;

/// <summary>
/// Raises bedsheet world-sprite depth when on top of a mob so the sheet renders above the target.
/// </summary>
public sealed partial class BlanketVisualSystem : EntitySystem
{
    private const int CoveredMobDepth = (int) DrawDepth.OverMobs;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, int> _originalDepth = new();
    private readonly HashSet<EntityUid> _entitiesInRange = new();
    private readonly List<EntityUid> _staleDepthKeys = new();
    private float _cleanupAccumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _cleanupAccumulator += frameTime;
        if (_cleanupAccumulator >= 1f)
        {
            _cleanupAccumulator = 0f;
            CleanupMissingBlankets();
        }

        var query = EntityQueryEnumerator<BlanketOverlayComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
            {
                RestoreDepth(uid, sprite);
                continue;
            }

            _entitiesInRange.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, 0.1f, _entitiesInRange);

            var overlapsMob = false;
            foreach (var entity in _entitiesInRange)
            {
                if (entity == uid)
                    continue;

                if (HasComp<MobStateComponent>(entity))
                {
                    overlapsMob = true;
                    break;
                }
            }

            if (!overlapsMob)
            {
                RestoreDepth(uid, sprite);
                continue;
            }

            if (!_originalDepth.ContainsKey(uid))
                _originalDepth[uid] = sprite.DrawDepth;

            if (sprite.DrawDepth < CoveredMobDepth)
                _sprite.SetDrawDepth((uid, sprite), CoveredMobDepth);
        }
    }

    private void RestoreDepth(EntityUid uid, SpriteComponent? sprite = null)
    {
        if (!_originalDepth.TryGetValue(uid, out var original))
            return;

        if (sprite == null && !TryComp(uid, out sprite))
        {
            _originalDepth.Remove(uid);
            return;
        }

        _sprite.SetDrawDepth((uid, sprite), original);
        _originalDepth.Remove(uid);
    }

    private void CleanupMissingBlankets()
    {
        if (_originalDepth.Count == 0)
            return;

        _staleDepthKeys.Clear();
        foreach (var uid in _originalDepth.Keys)
        {
            if (!EntityManager.EntityExists(uid))
                _staleDepthKeys.Add(uid);
        }

        foreach (var uid in _staleDepthKeys)
        {
            _originalDepth.Remove(uid);
        }
    }
}
