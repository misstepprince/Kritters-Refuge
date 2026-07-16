using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._CS.SpaceJanitor;

/// <summary>
/// This little weirdo goes through every item in space and, if its been there for a long time straight, deletes it.
/// This is to prevent space from being cluttered with debris and items that have been left behind.
/// This is a server-side system only, and does not need to be networked to clients.
/// </summary>
public sealed partial class SpaceJanitorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IChatManager _chat = default!;

    private const int MinutesBetweenChecks = 29;
    private const int MinutesBeforeCleanup = 1; // 12 hours
    private TimeSpan _nextCheck = TimeSpan.Zero;

    /// <inheritdoc/>
    public override void Initialize()
    {
        // penors
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _gameTiming.CurTime;
        if (curTime < _nextCheck)
            return;
        _nextCheck = curTime + TimeSpan.FromMinutes(MinutesBetweenChecks);
        var deleted = 0;
        var query = EntityQueryEnumerator<SpaceJanitorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!NakedAndInSpace(uid, comp))
            {
                // its not in space, so reset the timer.
                ResetNakedSpaceTime(uid, comp);
                continue;
            }

            UpdateNakedSpaceTime(
                uid,
                comp,
                curTime);
            if (DeleteIfNeeded(
                uid,
                comp,
                curTime))
            {
                deleted++;
            }
        }

        if (deleted == 0)
            return;

        var report = $"Space janitor cleanup sweep queued {deleted} entities for deletion.";
        Log.Info(report);
        _chat.SendAdminAnnouncement(report);
    }

    /// <summary>
    /// Checks if:
    /// The entity has null grid
    /// The entity is not inside something (has actual local coordinates)
    /// </summary>
    private bool NakedAndInSpace(EntityUid uid, SpaceJanitorComponent comp)
    {
        var xform = Transform(uid);
        if (_container.IsEntityOrParentInContainer(uid, xform: xform))
            return false;

        if (xform.LocalPosition == Vector2.Zero)
            return false;

        // clean up empty casings, whether in space or not, but only while not carried by something.
        if (comp.IsCasing
            && TryComp<CartridgeAmmoComponent>(uid, out var cartridge)
            && cartridge.Spent)
            return true; // naked, and in 'space' (on the floor)
        if (xform.GridUid != null)
            return false;
        return true;
    }

    private void UpdateNakedSpaceTime(EntityUid uid, SpaceJanitorComponent comp, TimeSpan curTime)
    {
        if (comp.FoundInSpaceTime == TimeSpan.Zero)
        {
            comp.FoundInSpaceTime = curTime;
        }
    }

    private void ResetNakedSpaceTime(EntityUid uid, SpaceJanitorComponent comp)
    {
        comp.FoundInSpaceTime = TimeSpan.Zero;
    }

    private bool DeleteIfNeeded(EntityUid uid, SpaceJanitorComponent comp, TimeSpan curTime)
    {
        if (comp.FoundInSpaceTime == TimeSpan.Zero)
            return false;
        if (curTime - comp.FoundInSpaceTime < TimeSpan.FromMinutes(MinutesBeforeCleanup))
            return false;
        // delete the entity.
        if (TryComp<EntityStorageComponent>(uid, out var storage)
            && !storage.DeleteContentsOnDestruction)
        {
            _entityStorage.EmptyContents(uid, storage);
        }
        if (TryComp<StorageComponent>(uid, out var storage2))
        {
            _container.EmptyContainer(storage2.Container);
        }
        var myCoords = Transform(uid).LocalPosition;
        Log.Info($"Space janitor sent entity {ToPrettyString(uid)} at {myCoords} to the shadow realm for being in space too long.");
        QueueDel(uid);
        return true;
    }
}
