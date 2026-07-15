using System.Numerics;
using Content.Server._CS.ForceFields.Components;
using Content.Server.Construction;
using Content.Server.Damage;
using Content.Server._NF.Power.EntitySystems;
using Content.Shared._CS.ForceFields.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._CS.ForceFields.EntitySystems;

public sealed partial class WallmountForceFieldGeneratorSystem : EntitySystem
{
    private static readonly TimeSpan FieldConstructFlashDelay = TimeSpan.FromMilliseconds(120);

    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private PowerReceiverSystem _powerReceiver = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, ReAnchorEvent>(OnReanchor);
        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, ComponentRemove>(OnComponentRemoved);
        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, RefreshPartsEvent>(OnRefreshParts, after: new[] { typeof(UpgradePowerSystem) });
        SubscribeLocalEvent<WallmountForceFieldGeneratorComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<WallmountLinkedForceFieldComponent, DamageChangedEvent>(OnLinkedFieldDamaged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WallmountForceFieldGeneratorComponent>();
        while (query.MoveNext(out var uid, out var generator))
        {
            Entity<WallmountForceFieldGeneratorComponent> ent = (uid, generator);
            var hasReceiver = TryComp<ApcPowerReceiverComponent>(uid, out var receiver);
            var receiverPowered = hasReceiver && receiver!.Powered;

            if (hasReceiver)
                UpdatePowerLoad(ent, receiver!, frameTime);

            if (TryComp<BatteryComponent>(uid, out var battery))
                UpdateBatteryCharge(ent, battery, receiverPowered, frameTime);

            if (!generator.Enabled)
            {
                if (generator.IsConnected)
                    RemoveConnections(ent);

                ChangeVisualizer(ent);
                continue;
            }

            if (!receiverPowered)
            {
                if (generator.IsConnected)
                    RemoveConnections(ent);

                ChangeVisualizer(ent);
                continue;
            }

            var xform = Transform(uid);
            if (!xform.Anchored)
            {
                if (generator.IsConnected)
                    RemoveConnections(ent);

                ChangeVisualizer(ent);
                continue;
            }

            TryGenerateConnections(ent, xform);
            ChangeVisualizer(ent);
        }
    }

    private void OnMapInit(Entity<WallmountForceFieldGeneratorComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<ApcPowerReceiverComponent>(ent, out var receiver) && ent.Comp.BasePowerLoad <= 0f)
            ent.Comp.BasePowerLoad = receiver.Load;

        ChangeVisualizer(ent);
    }

    private void OnRefreshParts(Entity<WallmountForceFieldGeneratorComponent> ent, ref RefreshPartsEvent args)
    {
        // UpgradePowerSystem runs first (after: ordering), so receiver.Load already has the upgrade-scaled active draw.
        if (TryComp<ApcPowerReceiverComponent>(ent, out var receiver))
            ent.Comp.BasePowerLoad = receiver.Load;
    }

    private void OnLinkedFieldDamaged(Entity<WallmountLinkedForceFieldComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        var damage = args.DamageDelta.GetTotal().Float();
        if (damage <= 0f)
            return;

        var generators = new List<Entity<WallmountForceFieldGeneratorComponent>>();
        foreach (var generatorUid in ent.Comp.Generators)
        {
            if (TryComp<WallmountForceFieldGeneratorComponent>(generatorUid, out var generatorComp) && generatorComp.Enabled)
                generators.Add((generatorUid, generatorComp));
        }

        if (generators.Count == 0)
            return;

        var splitDamage = damage / generators.Count;
        foreach (var generator in generators)
        {
            generator.Comp.PendingDamageDraw += splitDamage * generator.Comp.DamagePowerDrawPerDamage;
        }

        if (TryComp<DamageableComponent>(ent, out var damageable))
            _damageable.SetAllDamage(ent, damageable, 0);
    }

    private void OnInteractHand(Entity<WallmountForceFieldGeneratorComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        ToggleEnabled(ent, ent.Comp);
        args.Handled = true;
    }

    private void OnSignalReceived(Entity<WallmountForceFieldGeneratorComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port == ent.Comp.OffPort)
        {
            SetEnabled(ent, false, ent.Comp);
            return;
        }

        if (args.Port == ent.Comp.OnPort)
        {
            SetEnabled(ent, true, ent.Comp);
            return;
        }

        if (args.Port == ent.Comp.TogglePort)
            ToggleEnabled(ent, ent.Comp);
    }

    private void OnAnchorChanged(Entity<WallmountForceFieldGeneratorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            RemoveConnections(ent);

        ChangeVisualizer(ent);
    }

    private void OnReanchor(Entity<WallmountForceFieldGeneratorComponent> ent, ref ReAnchorEvent args)
    {
        GridCheck(ent);
    }

    private void OnComponentRemoved(Entity<WallmountForceFieldGeneratorComponent> ent, ref ComponentRemove args)
    {
        CancelActiveGlowDelay(ent.Comp);
        RemoveConnections(ent);
    }

    private void ToggleEnabled(EntityUid uid, WallmountForceFieldGeneratorComponent component)
    {
        SetEnabled(uid, !component.Enabled, component);
    }

    private void SetEnabled(EntityUid uid, bool enabled, WallmountForceFieldGeneratorComponent component)
    {
        if (enabled && !Transform(uid).Anchored)
            return;

        // Require active LV/APC power to switch on.
        if (enabled && (!TryComp<ApcPowerReceiverComponent>(uid, out var receiver) || !receiver.Powered))
            return;

        component.Enabled = enabled;
        if (!enabled && component.IsConnected)
            RemoveConnections((uid, component));

        ChangeVisualizer((uid, component));
    }

    private void TryGenerateConnections(Entity<WallmountForceFieldGeneratorComponent> generator, TransformComponent genXform)
    {
        var directions = Enum.GetValues<Direction>().Length;
        for (int i = 0; i < directions - 1; i += 2)
        {
            var dir = (Direction) i;

            if (generator.Comp.Connections.ContainsKey(dir))
                continue;

            TryGenerateConnection(dir, generator, genXform);
        }
    }

    private bool TryGenerateConnection(Direction dir, Entity<WallmountForceFieldGeneratorComponent> generator, TransformComponent gen1Xform)
    {
        var component = generator.Comp;
        if (!component.Enabled)
            return false;

        if (!gen1Xform.Anchored)
            return false;

        var genWorldPosRot = _transformSystem.GetWorldPositionRotation(gen1Xform);
        var dirRad = dir.ToAngle() + genWorldPosRot.WorldRotation;

        var ray = new CollisionRay(genWorldPosRot.WorldPosition, dirRad.ToVec(), component.CollisionMask);
        var rayResults = _physics.IntersectRay(gen1Xform.MapID, ray, component.MaxLength, generator, false);
        var generatorQuery = GetEntityQuery<WallmountForceFieldGeneratorComponent>();

        RayCastResults? closestResult = null;
        foreach (var result in rayResults)
        {
            if (!generatorQuery.HasComponent(result.HitEntity))
                continue;

            closestResult = result;
            break;
        }

        if (closestResult == null)
            return false;

        var otherUid = closestResult.Value.HitEntity;
        if (!TryComp<WallmountForceFieldGeneratorComponent>(otherUid, out var otherComp) ||
            otherComp == component ||
            !TryComp<PhysicsComponent>(otherUid, out var physicsComp) ||
            physicsComp.BodyType != BodyType.Static ||
            gen1Xform.ParentUid != Transform(otherUid).ParentUid)
        {
            return false;
        }

        if (!otherComp.Enabled || !TryComp<ApcPowerReceiverComponent>(otherUid, out var otherPowered) || !otherPowered.Powered)
            return false;

        var otherGenerator = (otherUid, otherComp);
        var fields = GenerateConnectionFields(dir, generator, otherGenerator);

        component.Connections[dir] = (otherGenerator, fields);
        otherComp.Connections[dir.GetOpposite()] = (generator, fields);

        component.IsConnected = true;
        otherComp.IsConnected = true;
        ChangeVisualizer(generator);
        ChangeVisualizer(otherGenerator);

        return true;
    }

    private List<EntityUid> GenerateConnectionFields(Direction connectionDirection, Entity<WallmountForceFieldGeneratorComponent> firstGen, Entity<WallmountForceFieldGeneratorComponent> secondGen)
    {
        var fields = new List<EntityUid>();

        var gen1Coords = Transform(firstGen).Coordinates;
        var gen2Coords = Transform(secondGen).Coordinates;

        var delta = (gen2Coords - gen1Coords).Position;
        var dirVec = delta.Normalized();
        var stopDist = delta.Length();
        var currentOffset = dirVec;

        while (currentOffset.Length() < stopDist)
        {
            var currentCoords = gen1Coords.Offset(currentOffset);
            Spawn("EffectRCDConstruct0", currentCoords);

            Robust.Shared.Timing.Timer.Spawn(FieldConstructFlashDelay, () =>
            {
                if (Deleted(firstGen) || Deleted(secondGen))
                    return;

                if (!TryComp<WallmountForceFieldGeneratorComponent>(firstGen, out var firstComp) ||
                    !TryComp<WallmountForceFieldGeneratorComponent>(secondGen, out var secondComp))
                {
                    return;
                }

                if (!firstComp.Enabled || !secondComp.Enabled)
                    return;

                if (!firstComp.Connections.TryGetValue(connectionDirection, out var connection) || connection.Item1.Owner != secondGen.Owner)
                    return;

                var field = Spawn(firstComp.CreatedField, currentCoords);

                var fieldXform = Transform(field);
                _transformSystem.SetParent(field, fieldXform, firstGen);

                var linkedField = EnsureComp<WallmountLinkedForceFieldComponent>(field);
                linkedField.Generators.Clear();
                linkedField.Generators.Add(firstGen);
                linkedField.Generators.Add(secondGen);

                if (dirVec.GetDir() == Direction.East || dirVec.GetDir() == Direction.West)
                {
                    var angle = fieldXform.LocalPosition.ToAngle();
                    fieldXform.LocalRotation = Angle.FromDegrees(angle.Degrees + 90);
                }

                DisplaceIntersectingEntities(field, dirVec, firstGen.Owner, secondGen.Owner);
                connection.Item2.Add(field);
            });

            currentOffset += dirVec;
        }

        return fields;
    }

    private void UpdatePowerLoad(Entity<WallmountForceFieldGeneratorComponent> generator,
        ApcPowerReceiverComponent receiver,
        float frameTime)
    {
        var extraLoad = generator.Comp.PendingDamageDraw;

        // While connected, draw the full upgrade-scaled active load plus any damage spike.
        // While idle (no fields), draw a minimal standby load.
        if (generator.Comp.IsConnected)
            _powerReceiver.SetLoad(receiver, generator.Comp.BasePowerLoad + extraLoad);
        else
            _powerReceiver.SetLoad(receiver, generator.Comp.IdlePowerLoad);

        if (extraLoad > 0f && TryComp<BatteryComponent>(generator.Owner, out var battery))
            _battery.UseCharge(generator.Owner, extraLoad * frameTime, battery);

        if (extraLoad <= 0f)
            return;

        generator.Comp.PendingDamageDraw = MathF.Max(0f, extraLoad - (extraLoad * frameTime));
    }

    private void UpdateBatteryCharge(Entity<WallmountForceFieldGeneratorComponent> generator,
        BatteryComponent battery,
        bool receiverPowered,
        float frameTime)
    {
        if (generator.Comp.Enabled && generator.Comp.IsConnected)
            _battery.UseCharge(generator.Owner, generator.Comp.BatteryDrainConnected * frameTime, battery);

        if (receiverPowered && battery.CurrentCharge < battery.MaxCharge)
            _battery.TrySetCharge(generator.Owner, battery.CurrentCharge + generator.Comp.BatteryChargeRate * frameTime, battery);
    }

    private void DisplaceIntersectingEntities(EntityUid field, Vector2 dirVec, EntityUid firstGen, EntityUid secondGen)
    {
        var intersecting = new HashSet<EntityUid>();
        _lookup.GetEntitiesIntersecting(field, intersecting, LookupFlags.Dynamic | LookupFlags.Sundries);

        foreach (var entity in intersecting)
        {
            if (entity == field || entity == firstGen || entity == secondGen)
                continue;

            if (!TryComp(entity, out TransformComponent? xform) || xform.Anchored)
                continue;

            if (xform.ParentUid == field)
                continue;

            if (HasComp<ContainerManagerComponent>(entity))
                continue;

            var fieldPos = _transformSystem.GetWorldPosition(field);
            var entityPos = _transformSystem.GetWorldPosition(entity);

            Direction side;
            if (MathF.Abs(dirVec.X) > MathF.Abs(dirVec.Y))
                side = entityPos.Y >= fieldPos.Y ? Direction.North : Direction.South;
            else
                side = entityPos.X >= fieldPos.X ? Direction.East : Direction.West;

            var offset = side switch
            {
                Direction.North => new Vector2(0f, 1f),
                Direction.South => new Vector2(0f, -1f),
                Direction.East => new Vector2(1f, 0f),
                Direction.West => new Vector2(-1f, 0f),
                _ => Vector2.Zero,
            };

            _transformSystem.SetCoordinates(entity, Transform(field).Coordinates.Offset(offset));
        }
    }

    private void GridCheck(Entity<WallmountForceFieldGeneratorComponent> generator)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var (_, linked) in generator.Comp.Connections)
        {
            var gen1Parent = xformQuery.GetComponent(generator).ParentUid;
            var gen2Parent = xformQuery.GetComponent(linked.Item1).ParentUid;

            if (gen1Parent != gen2Parent)
                RemoveConnections(generator);
        }
    }

    private void RemoveConnections(Entity<WallmountForceFieldGeneratorComponent> generator)
    {
        var (_, component) = generator;

        foreach (var (direction, value) in component.Connections)
        {
            foreach (var field in value.Item2)
            {
                QueueDel(field);
            }

            value.Item1.Comp.Connections.Remove(direction.GetOpposite());
            if (value.Item1.Comp.Connections.Count == 0)
            {
                value.Item1.Comp.IsConnected = false;
                ChangeVisualizer(value.Item1);
            }
        }

        component.Connections.Clear();
        component.IsConnected = false;
        ChangeVisualizer(generator);
    }

    private void ChangeVisualizer(Entity<WallmountForceFieldGeneratorComponent> ent)
    {
        _ambientSound.SetAmbience(ent, ent.Comp.IsConnected);

        // Delay active glow when fields come online to reduce flash spam under unstable power.
        if (!ent.Comp.IsConnected)
        {
            CancelActiveGlowDelay(ent.Comp);
            ent.Comp.ActiveGlowVisible = false;
        }
        else if (!ent.Comp.ActiveGlowVisible)
        {
            if (ent.Comp.ActiveGlowDelay <= TimeSpan.Zero)
            {
                ent.Comp.ActiveGlowVisible = true;
            }
            else if (ent.Comp.ActiveGlowDelayCancel == null)
            {
                ent.Comp.ActiveGlowDelayCancel = new System.Threading.CancellationTokenSource();
                var token = ent.Comp.ActiveGlowDelayCancel.Token;
                Robust.Shared.Timing.Timer.Spawn(ent.Comp.ActiveGlowDelay, () =>
                {
                    if (Deleted(ent))
                        return;

                    if (!TryComp<WallmountForceFieldGeneratorComponent>(ent, out var generator))
                        return;

                    generator.ActiveGlowDelayCancel = null;
                    if (!generator.IsConnected)
                        return;

                    generator.ActiveGlowVisible = true;
                    ApplyGlowVisuals(ent, true);
                }, token);
            }
        }

        ApplyGlowVisuals(ent, ent.Comp.ActiveGlowVisible);

        var warning = false;
        if (TryComp<BatteryComponent>(ent, out var battery) && battery.MaxCharge > 0f)
            warning = battery.CurrentCharge < battery.MaxCharge;

        _appearance.SetData(ent, WallmountForceFieldGeneratorVisuals.WarningLight, warning);
    }

    private void ApplyGlowVisuals(Entity<WallmountForceFieldGeneratorComponent> ent, bool active)
    {
        _appearance.SetData(ent, WallmountForceFieldGeneratorVisuals.OnLight, active);

        // Expand the point light when fields are active, shrink back to soft idle glow otherwise
        if (active)
        {
            _pointLight.SetRadius(ent, 24f);
            _pointLight.SetEnergy(ent, 2.0f);
        }
        else
        {
            _pointLight.SetRadius(ent, 3f);
            _pointLight.SetEnergy(ent, 0.4f);
        }
    }

    private static void CancelActiveGlowDelay(WallmountForceFieldGeneratorComponent component)
    {
        component.ActiveGlowDelayCancel?.Cancel();
        component.ActiveGlowDelayCancel = null;
    }
}
