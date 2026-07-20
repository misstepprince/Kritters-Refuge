using System.Linq;
using Content.Shared.Clothing.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Content.Shared.Tools.Components;
using Content.Shared.DoAfter;
using Content.Shared.Verbs;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared.Clothing.EntitySystems;

/// <summary>
/// System for handling advanced collar module installation and removal.
/// </summary>
public sealed partial class AdvancedCollarSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ISerializationManager _serializationManager = default!;

    public const string ModuleContainerName = "collar_module_container";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AdvancedCollarComponent, ComponentInit>(OnCollarInit);
        SubscribeLocalEvent<AdvancedCollarComponent, ComponentShutdown>(OnCollarShutdown);
        SubscribeLocalEvent<AdvancedCollarComponent, MapInitEvent>(OnCollarMapInit);
        SubscribeLocalEvent<AdvancedCollarComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<AdvancedCollarComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<AdvancedCollarComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<AdvancedCollarComponent, AdvancedCollarRemoveModulesDoAfterEvent>(OnRemoveModulesComplete);
        SubscribeLocalEvent<AdvancedCollarComponent, EntInsertedIntoContainerMessage>(OnModuleInserted);
        SubscribeLocalEvent<AdvancedCollarComponent, EntRemovedFromContainerMessage>(OnModuleRemoved);

        SubscribeLocalEvent<AdvancedCollarModuleComponent, ExaminedEvent>(OnModuleExamined);
    }

    private void OnCollarInit(Entity<AdvancedCollarComponent> collar, ref ComponentInit args)
    {
        collar.Comp.ModuleContainer = _container.EnsureContainer<Container>(collar, ModuleContainerName);
    }

    private void OnCollarMapInit(Entity<AdvancedCollarComponent> collar, ref MapInitEvent args)
    {
        ReconcileModuleEffects(collar);
    }

    private void OnCollarShutdown(Entity<AdvancedCollarComponent> collar, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(collar))
            return;

        foreach (var moduleUid in collar.Comp.ModuleContainer.ContainedEntities)
        {
            if (TryComp<AdvancedCollarModuleComponent>(moduleUid, out var module))
                SetInstalledIn((moduleUid, module), null);
        }

        foreach (var componentName in collar.Comp.ModuleOwnedComponents)
        {
            if (_componentFactory.TryGetRegistration(componentName, out var registration))
                RemComp(collar, registration.Type);
        }

        collar.Comp.ModuleOwnedComponents.Clear();
    }

    private void OnModuleInserted(Entity<AdvancedCollarComponent> collar, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ModuleContainerName)
            return;

        if (!TryComp<AdvancedCollarModuleComponent>(args.Entity, out var module))
            return;

        SetInstalledIn((args.Entity, module), collar);
        ReconcileModuleEffects(collar);
    }

    private void OnModuleRemoved(Entity<AdvancedCollarComponent> collar, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ModuleContainerName)
            return;

        if (!TryComp<AdvancedCollarModuleComponent>(args.Entity, out var module))
            return;

        SetInstalledIn((args.Entity, module), null);
        ReconcileModuleEffects(collar);
    }

    private void OnExamined(Entity<AdvancedCollarComponent> collar, ref ExaminedEvent args)
    {
        var moduleCount = collar.Comp.ModuleContainer.ContainedEntities.Count;

        if (moduleCount == 0)
        {
            args.PushMarkup(Loc.GetString("advanced-collar-examine-no-modules"));
        }
        else
        {
            args.PushMarkup(Loc.GetString("advanced-collar-examine-modules",
                ("count", moduleCount),
                ("max", collar.Comp.MaxModules)));

            foreach (var moduleUid in collar.Comp.ModuleContainer.ContainedEntities)
            {
                if (TryComp<AdvancedCollarModuleComponent>(moduleUid, out var module) &&
                    !string.IsNullOrEmpty(module.ModuleDescription))
                {
                    args.PushMarkup(Loc.GetString("advanced-collar-examine-module-entry",
                        ("name", Name(moduleUid)),
                        ("description", module.ModuleDescription)));
                }
            }
        }
    }

    private void OnModuleExamined(Entity<AdvancedCollarModuleComponent> module, ref ExaminedEvent args)
    {
        if (!string.IsNullOrEmpty(module.Comp.ModuleDescription))
        {
            args.PushMarkup(Loc.GetString("advanced-collar-module-examine",
                ("description", module.Comp.ModuleDescription)));
        }

        if (module.Comp.InstalledIn != null)
        {
            args.PushMarkup(Loc.GetString("advanced-collar-module-already-in-collar"));
        }
    }

    private void OnInteractUsing(Entity<AdvancedCollarComponent> collar, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Try to install a module
        if (TryComp<AdvancedCollarModuleComponent>(args.Used, out var module))
        {
            args.Handled = true;
            TryInstallModule(collar, (args.Used, module), args.User);
            return;
        }

        // Try to remove modules with screwdriver
        if (_tool.HasQuality(args.Used, "Screwing"))
        {
            if (collar.Comp.ModuleContainer.ContainedEntities.Count == 0)
                return;

            args.Handled = true;

            // Start do-after for removing modules
            var doAfterArgs = new DoAfterArgs(EntityManager, args.User, 3f, new AdvancedCollarRemoveModulesDoAfterEvent(), collar.Owner, target: collar.Owner, used: args.Used)
            {
                BreakOnMove = true,
                NeedHand = true
            };

            _doAfter.TryStartDoAfter(doAfterArgs);
        }
    }

    private void OnGetVerbs(Entity<AdvancedCollarComponent> collar, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (collar.Comp.ModuleContainer.ContainedEntities.Count == 0)
            return;

        // Check if user has a screwdriver
        if (args.Using == null || !_tool.HasQuality(args.Using.Value, "Screwing"))
            return;

        // Capture values for lambda
        var user = args.User;
        var used = args.Using.Value;
        var collarUid = collar.Owner;

        InteractionVerb verb = new()
        {
            Act = () =>
            {
                // Start do-after for removing modules
                var doAfterArgs = new DoAfterArgs(EntityManager, user, 3f, new AdvancedCollarRemoveModulesDoAfterEvent(), collarUid, target: collarUid, used: used)
                {
                    BreakOnMove = true,
                    NeedHand = true
                };

                _doAfter.TryStartDoAfter(doAfterArgs);
            },
            Text = Loc.GetString("advanced-collar-remove-modules-verb"),
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
            Priority = 1
        };

        args.Verbs.Add(verb);
    }

    private void OnRemoveModulesComplete(Entity<AdvancedCollarComponent> collar, ref AdvancedCollarRemoveModulesDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // Check if collar is being worn - can't remove modules while equipped
        if (TryComp<ClothingComponent>(collar, out var clothing) && clothing.InSlot != null)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-worn"), collar, args.User);
            return;
        }

        RemoveAllModules(collar, args.User);
    }

    public void TryInstallModule(Entity<AdvancedCollarComponent> collar, Entity<AdvancedCollarModuleComponent> module, EntityUid user)
    {
        // Check if module is already installed somewhere
        if (module.Comp.InstalledIn != null)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-module-already-installed"), collar, user);
            return;
        }

        // Check if collar is full
        if (collar.Comp.ModuleContainer.ContainedEntities.Count >= collar.Comp.MaxModules)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-full"), collar, user);
            return;
        }

        // Install the module
        if (_container.Insert(module.Owner, collar.Comp.ModuleContainer))
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-module-installed",
                ("module", Name(module))), collar, user);
            _audio.PlayPredicted(collar.Comp.ModuleInsertionSound, collar, user);
        }
    }

    private void SetInstalledIn(Entity<AdvancedCollarModuleComponent> module, EntityUid? collar)
    {
        if (module.Comp.InstalledIn == collar || TerminatingOrDeleted(module))
            return;

        module.Comp.InstalledIn = collar;
        Dirty(module);
    }

    private void ReconcileModuleEffects(Entity<AdvancedCollarComponent> collar)
    {
        if (TerminatingOrDeleted(collar))
            return;

        var requested = new Dictionary<Type, (string Name, EntityUid Module)>();
        foreach (var moduleUid in collar.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<AdvancedCollarModuleComponent>(moduleUid, out var module))
                continue;

            SetInstalledIn((moduleUid, module), collar);
            AddRequestedComponent(module.ComponentToAdd, moduleUid, requested);

            foreach (var componentName in module.ComponentsToAdd)
                AddRequestedComponent(componentName, moduleUid, requested);
        }

        var changed = false;
        foreach (var componentName in collar.Comp.ModuleOwnedComponents.ToArray())
        {
            // Missing registrations are expected client-side for server-only components.
            if (!_componentFactory.TryGetRegistration(componentName, out var registration) ||
                requested.ContainsKey(registration.Type))
                continue;

            RemComp(collar, registration.Type);
            collar.Comp.ModuleOwnedComponents.Remove(componentName);
            changed = true;
        }

        foreach (var (componentType, request) in requested)
        {
            if (HasComp(collar, componentType))
                continue;

            AddModuleComponent(collar, request.Module, componentType);
            changed |= collar.Comp.ModuleOwnedComponents.Add(request.Name);
        }

        if (changed)
            Dirty(collar);
    }

    private void AddRequestedComponent(
        string? componentName,
        EntityUid module,
        Dictionary<Type, (string Name, EntityUid Module)> requested)
    {
        if (string.IsNullOrEmpty(componentName) ||
            !_componentFactory.TryGetRegistration(componentName, out var registration))
            return;

        requested.TryAdd(registration.Type, (registration.Name, module));
    }

    private void AddModuleComponent(EntityUid collar, EntityUid module, Type componentType)
    {
        var component = (Component)_componentFactory.GetComponent(componentType);
        if (EntityManager.TryGetComponent(module, componentType, out var moduleComponent))
        {
            var copy = (object)component;
            _serializationManager.CopyTo(moduleComponent, ref copy);
            component = (Component)copy!;
        }

        AddComp(collar, component);
    }

    public void RemoveAllModules(Entity<AdvancedCollarComponent> collar, EntityUid user)
    {
        var moduleCount = collar.Comp.ModuleContainer.ContainedEntities.Count;

        if (moduleCount == 0)
        {
            _popup.PopupClient(Loc.GetString("advanced-collar-no-modules"), collar, user);
            return;
        }

        // Store modules before emptying container
        var modulesToClear = collar.Comp.ModuleContainer.ContainedEntities.ToList();

        // Remove all modules from the container
        _container.EmptyContainer(collar.Comp.ModuleContainer);

        // Eject each module to user's hands or drop nearby
        foreach (var moduleUid in modulesToClear)
        {
            _hands.PickupOrDrop(user, moduleUid, dropNear: true);
        }

        _popup.PopupClient(Loc.GetString("advanced-collar-modules-removed",
            ("count", moduleCount)), collar, user);
        _audio.PlayPredicted(collar.Comp.ModuleExtractionSound, collar, user);
    }
}
