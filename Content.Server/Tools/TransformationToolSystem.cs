using System.Linq;
using Content.Server.Consent;
using Content.Server.Polymorph.Systems;
using Content.Server.Polymorph.Components;
using Content.Shared.Consent;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Tools;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Melee;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Server.Tools;

// *********
// Created solely to tease Cuffle (Robyn) helplessly
// Love ya Cuffle <3
// Rico of CoyoteSector - November 2025
// *********

// *********
// Fuck you Rico
// Cuffle (Robyn) of CoyoteSector - November 2025
// *********

public sealed partial class TransformationToolSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private PolymorphSystem _polymorph = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private ConsentSystem _consent = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    private static readonly ProtoId<ConsentTogglePrototype> TransformationConsent = "Transformation";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TransformationToolComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<TransformationToolComponent, AfterActivatableUIOpenEvent>(OnUIOpened);
        SubscribeLocalEvent<TransformationToolComponent, EntityTerminatingEvent>(OnToolTerminating);

        SubscribeLocalEvent<TransformationToolComponent, TransformationToolClearScanMessage>(OnClearScan);
        SubscribeLocalEvent<TransformationToolComponent, TransformationToolRevertMessage>(OnRevert);
        SubscribeLocalEvent<TransformationToolComponent, TransformationToolRevertAllMessage>(OnRevertAll);
        SubscribeLocalEvent<TransformationToolComponent, TransformationToolSetDurationMessage>(OnSetDuration);

        SubscribeLocalEvent<PolymorphedEntityComponent, EntityTerminatingEvent>(OnPolymorphedEntityTerminating);
    }

    private void OnAfterInteract(EntityUid uid, TransformationToolComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        var target = args.Target.Value;

        // If we have no scanned entity, scan the target
        if (component.ScannedEntity == null)
        {
            ScanEntity(uid, component, target, args.User);
            args.Handled = true;
            return;
        }

        // If we have a scanned entity and target is a mob with a mind, transform them
        if (HasComp<Shared.Mind.Components.MindContainerComponent>(target))
        {
            TransformEntity(uid, component, target, args.User);
            args.Handled = true;
        }
    }

    private void ScanEntity(EntityUid tool, TransformationToolComponent component, EntityUid target, EntityUid user)
    {
        // Don't scan the tool itself
        if (target == tool)
            return;

        // Don't scan living mobs (prevents issues with complex entity types like turrets, NPCs, etc.)
        if (HasComp<Shared.Mobs.Components.MobStateComponent>(target))
        {
            _popup.PopupEntity("Cannot scan living entities!", tool, user, PopupType.Medium);
            return;
        }

        // Check blacklist tags
        if (component.BlacklistTags.Count > 0)
        {
            foreach (var tag in component.BlacklistTags)
            {
                if (_tag.HasTag(target, tag))
                {
                    _popup.PopupEntity("This entity cannot be scanned!", tool, user, PopupType.Medium);
                    return;
                }
            }
        }

        // Check whitelist tags (if any are specified)
        if (component.WhitelistTags.Count > 0)
        {
            var hasWhitelistTag = false;
            foreach (var tag in component.WhitelistTags)
            {
                if (_tag.HasTag(target, tag))
                {
                    hasWhitelistTag = true;
                    break;
                }
            }

            if (!hasWhitelistTag)
            {
                _popup.PopupEntity("This entity cannot be scanned!", tool, user, PopupType.Medium);
                return;
            }
        }

        var metaData = MetaData(target);

        component.ScannedEntity = target;
        component.ScannedPrototype = metaData.EntityPrototype?.ID;
        component.ScannedName = metaData.EntityName;

        if (component.ScanSound != null)
            _audio.PlayPvs(component.ScanSound, tool);

        _popup.PopupEntity($"Scanned: {component.ScannedName}", tool, user);

        Dirty(tool, component);
        UpdateUI(tool, component);
    }

    private void TransformEntity(EntityUid tool, TransformationToolComponent component, EntityUid target, EntityUid user)
    {
        if (component.ScannedPrototype == null)
        {
            _popup.PopupEntity("No entity scanned!", tool, user, PopupType.Medium);
            return;
        }

        // Prevent self-transformation
        if (target == user)
        {
            _popup.PopupEntity("You cannot transform yourself!", tool, user, PopupType.Medium);
            return;
        }

        // Check if target has consented to transformations
        if (!_consent.HasConsent(target, TransformationConsent))
        {
            _popup.PopupEntity($"{MetaData(target).EntityName} has not consented to transformations!", tool, user, PopupType.Medium);
            return;
        }

        // Check if already transformed
        if (component.ActiveTransformations.ContainsKey(target))
        {
            _popup.PopupEntity($"{MetaData(target).EntityName} is already transformed!", tool, user, PopupType.Medium);
            return;
        }

        // Convert minutes to seconds for polymorph system
        var durationSeconds = component.DefaultDurationMinutes * 60f;

        // Create a temporary polymorph prototype
        var polymorphConfig = new PolymorphConfiguration
        {
            Entity = component.ScannedPrototype,
            Duration = durationSeconds > 0 ? (int)durationSeconds : null,
            Forced = false,
            TransferDamage = true,
            TransferName = true,
            TransferHumanoidAppearance = false,
            Inventory = PolymorphInventoryChange.Drop, // Drop items since target may not have inventory
            RevertOnCrit = false,
            RevertOnDeath = false,
            RevertOnEat = false,
            AllowRepeatedMorphs = false,
        };

        var transformed = _polymorph.PolymorphEntity(target, polymorphConfig);

        if (transformed != null)
        {
            component.ActiveTransformations[transformed.Value] = target;

            // Check if the scanned entity is a plushie and restrict movement
            if (component.ScannedEntity != null && IsPlushie(component.ScannedEntity.Value))
            {
                // Remove movement capability by setting speed to 0
                EnsureComp<MovementSpeedModifierComponent>(transformed.Value);
                _movement.ChangeBaseSpeed(transformed.Value, 0, 0, 20);
            }

            if (component.TransformSound != null)
                _audio.PlayPvs(component.TransformSound, tool);

            _popup.PopupEntity($"Transformed {MetaData(target).EntityName} into {component.ScannedName}!", tool, user);

            Dirty(tool, component);
            UpdateUI(tool, component);
        }
    }

    private void OnUIOpened(EntityUid uid, TransformationToolComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateUI(uid, component);
    }

    private void OnPolymorphedEntityTerminating(EntityUid uid, PolymorphedEntityComponent component, ref EntityTerminatingEvent args)
    {
        // When a polymorphed entity is being deleted, remove it from any transformation tools tracking it
        var query = EntityQueryEnumerator<TransformationToolComponent>();
        while (query.MoveNext(out var toolUid, out var tool))
        {
            if (tool.ActiveTransformations.Remove(uid))
            {
                Dirty(toolUid, tool);
                UpdateUI(toolUid, tool);
            }
        }
    }

    private void OnToolTerminating(EntityUid uid, TransformationToolComponent component, ref EntityTerminatingEvent args)
    {
        // Clean up when the tool itself is being deleted
        component.ActiveTransformations.Clear();
    }

    private void OnClearScan(EntityUid uid, TransformationToolComponent component, TransformationToolClearScanMessage args)
    {
        component.ScannedEntity = null;
        component.ScannedPrototype = null;
        component.ScannedName = null;

        Dirty(uid, component);
        UpdateUI(uid, component);
    }

    private void OnRevert(EntityUid uid, TransformationToolComponent component, TransformationToolRevertMessage args)
    {
        var target = GetEntity(args.Target);

        // Verify entity exists and is in our tracking dictionary
        if (!Exists(target))
            return;

        if (component.ActiveTransformations.ContainsKey(target))
        {
            // Stop any active melee weapon attacks before reverting to prevent client-side rendering issues
            if (TryComp<MeleeWeaponComponent>(target, out var meleeWeapon))
            {
                meleeWeapon.Attacking = false;
                Dirty(target, meleeWeapon);
            }

            // Revert will trigger entity deletion, which will trigger OnPolymorphedEntityTerminating
            // which will clean up the dictionary entry for us
            _polymorph.Revert(target);

            // Don't update UI here - let OnPolymorphedEntityTerminating handle it
            // to avoid race conditions with entity deletion
        }
    }

    private void OnRevertAll(EntityUid uid, TransformationToolComponent component, TransformationToolRevertAllMessage args)
    {
        foreach (var (transformed, original) in component.ActiveTransformations.ToList())
        {
            if (Exists(transformed))
                _polymorph.Revert(transformed);
        }

        // The dictionary will be cleaned up by OnPolymorphedEntityTerminating as each entity is deleted
    }

    private void OnSetDuration(EntityUid uid, TransformationToolComponent component, TransformationToolSetDurationMessage args)
    {
        component.DefaultDurationMinutes = Math.Clamp(args.DurationMinutes, 0, 4320); // Max 4320 minutes (72 hours)
        Dirty(uid, component);
        UpdateUI(uid, component);
    }

    private void UpdateUI(EntityUid uid, TransformationToolComponent component, EntityUid? user = null)
    {
        if (!_ui.HasUi(uid, TransformationToolUiKey.Key))
            return;

        // Clean up any stale entries (deleted or terminating entities)
        var toRemove = new List<EntityUid>();
        foreach (var (transformed, original) in component.ActiveTransformations)
        {
            if (!Exists(transformed) || LifeStage(transformed) >= EntityLifeStage.Terminating ||
                !Exists(original) || LifeStage(original) >= EntityLifeStage.Terminating)
            {
                toRemove.Add(transformed);
            }
        }

        foreach (var entity in toRemove)
        {
            component.ActiveTransformations.Remove(entity);
        }

        if (toRemove.Count > 0)
            Dirty(uid, component);

        var netTransformations = new Dictionary<NetEntity, NetEntity>();
        foreach (var (transformed, original) in component.ActiveTransformations)
        {
            // Double-check entities are valid and fully initialized
            if (Exists(transformed) && LifeStage(transformed) == EntityLifeStage.MapInitialized &&
                Exists(original) && LifeStage(original) == EntityLifeStage.MapInitialized)
            {
                netTransformations[GetNetEntity(transformed)] = GetNetEntity(original);
            }
        }

        var state = new TransformationToolBoundUserInterfaceState(
            component.ScannedName,
            component.ScannedPrototype,
            netTransformations,
            component.DefaultDurationMinutes
        );

        _ui.SetUiState(uid, TransformationToolUiKey.Key, state);
    }

    /// <summary>
    /// Checks if an entity is a plushie by checking if it or any of its parent prototypes is "BasePlushie"
    /// </summary>
    private bool IsPlushie(EntityUid entity)
    {
        if (!TryComp<MetaDataComponent>(entity, out var metaData))
            return false;

        var prototype = metaData.EntityPrototype;
        if (prototype == null)
            return false;

        // Check if this prototype is BasePlushie
        if (prototype.ID == "BasePlushie" || prototype.ID == "BasePlushieVulp")
            return true;

        // Check if any parent prototype is BasePlushie
        if (prototype.Parents != null)
        {
            foreach (var parentId in prototype.Parents)
            {
                if (parentId == "BasePlushie" || parentId == "BasePlushieVulp")
                    return true;

                // Recursively check parent's parents
                if (_prototype.TryIndex<EntityPrototype>(parentId, out var parentProto))
                {
                    if (HasParentPrototype(parentProto, "BasePlushie") || HasParentPrototype(parentProto, "BasePlushieVulp"))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if a prototype has a specific parent in its inheritance chain
    /// </summary>
    private bool HasParentPrototype(EntityPrototype prototype, string targetParentId)
    {
        if (prototype.ID == targetParentId)
            return true;

        if (prototype.Parents == null)
            return false;

        foreach (var parentId in prototype.Parents)
        {
            if (parentId == targetParentId)
                return true;

            if (_prototype.TryIndex<EntityPrototype>(parentId, out var parentProto))
            {
                if (HasParentPrototype(parentProto, targetParentId))
                    return true;
            }
        }

        return false;
    }
}
