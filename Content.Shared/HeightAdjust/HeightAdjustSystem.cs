using System;
using System.Linq;
using System.Numerics;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Components;
using Content.Shared.Nyanotrasen.Item.PseudoItem;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.HeightAdjust;

public sealed class HeightAdjustSystem : EntitySystem
{
    [Dependency] private readonly SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private readonly FixtureSystem _fixtures = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly INetManager _net = default!;

    private const float BagSizeThreshold = 0.75f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, RequestSizeRecalcEvent>(OnRequestSizeRecalc);
    }

    /// <summary>
    /// Handles requests to recalculate an entity's size by collecting all active modifiers
    /// and applying the final combined scale.
    /// </summary>
    private void OnRequestSizeRecalc(EntityUid target, HumanoidAppearanceComponent component, ref RequestSizeRecalcEvent ev)
    {
        // Collect all size modifiers from various systems
        var getModifiersEvent = new GetSizeModifierEvent(target);
        RaiseLocalEvent(target, ref getModifiersEvent);

        // Calculate final scale by multiplying all modifiers
        float finalScale = 1.0f;

        // Sort by priority (lower priority applied first, so higher priority can override)
        var sortedModifiers = getModifiersEvent.Modifiers.OrderBy(m => m.Priority).ToList();

        foreach (var modifier in sortedModifiers)
        {
            finalScale *= modifier.Scale;
        }

        // Apply the final scale, bypassing species limits for temporary effects
        SetScale(target, finalScale, bypassLimits: true);
    }


    /// <summary>
    ///     Changes the visual scale and mass based on a provided float scale
    /// </summary>
    /// <param name="uid">The entity to modify values for</param>
    /// <param name="scale">The scale multiplier to apply to base height/width</param>
    /// <param name="bypassLimits">Whether to bypass species min/max limits (for temporary effects)</param>
    /// <returns>True if all operations succeeded</returns>
    public bool SetScale(EntityUid uid, float scale, bool bypassLimits = false)
    {
        var succeeded = false;

        // Apply visual scaling
        if (EntityManager.TryGetComponent<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            // Multiply the base height/width by the scale modifier
            var newHeight = humanoid.BaseHeight * scale;
            var newWidth = humanoid.BaseWidth * scale;

            _appearance.SetHeight(uid, newHeight, bypassLimits: bypassLimits, humanoid: humanoid);
            _appearance.SetWidth(uid, newWidth, bypassLimits: bypassLimits, humanoid: humanoid);
            UpdateBagEligibility(uid, humanoid);
            succeeded = true;
        }

        // Apply mass scaling by adjusting fixture densities
        // Mass should scale with volume (scale^2 in 2D physics representing 3D objects)
        if (EntityManager.TryGetComponent<FixturesComponent>(uid, out var fixtures) &&
            EntityManager.TryGetComponent<PhysicsComponent>(uid, out var physics))
        {
            var sizeComp = EntityManager.EnsureComponent<SizeAffectedComponent>(uid);

            foreach (var (id, fixture) in fixtures.Fixtures.ToArray())
            {
                // Store original density on first scaling
                if (!sizeComp.OriginalFixtureDensities.ContainsKey(id))
                {
                    sizeComp.OriginalFixtureDensities[id] = fixture.Density;
                }

                var originalDensity = sizeComp.OriginalFixtureDensities[id];

                // Scale density by scale^2 to make mass scale with "volume" in 2D
                // Since Area = originalArea * scale^2, and Mass = Density * Area
                // To get Mass = originalMass * scale^2, we need Density = originalDensity * scale^2 / scale^2 = originalDensity
                // Wait no - we want new mass, so: newMass = originalMass * scale^2
                // newMass = newDensity * newArea = newDensity * (originalArea * scale^2)
                // originalMass * scale^2 = newDensity * (originalArea * scale^2)
                // originalDensity * originalArea * scale^2 = newDensity * originalArea * scale^2
                // So newDensity = originalDensity (density stays constant, mass scales with area)

                // Actually for proper 3D->2D representation, mass should scale with scale^2
                // Area already scales with fixture radius changes (if we were scaling hitboxes)
                // But since we're NOT scaling hitboxes, we need to scale density to compensate
                // newMass = newDensity * originalArea
                // We want: newMass = originalMass * scale^2
                // So: newDensity * originalArea = originalDensity * originalArea * scale^2
                // Therefore: newDensity = originalDensity * scale^2

                var newDensity = originalDensity * scale * scale;
                _physics.SetDensity(uid, id, fixture, newDensity, false, fixtures);
            }

            // Recalculate mass after all density changes
            _fixtures.FixtureUpdate(uid, manager: fixtures, body: physics);
            succeeded = true;
        }

        return succeeded;
    }

    /// <summary>
    ///     Changes the visual scale based on a provided Vector2 scale
    /// </summary>
    /// <param name="uid">The entity to modify values for</param>
    /// <param name="scale">The base scale to set (X = width, Y = height). This sets BaseHeight/BaseWidth.</param>
    /// <returns>True if all operations succeeded</returns>
    public bool SetScale(EntityUid uid, Vector2 scale)
    {
        if (EntityManager.TryGetComponent<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            // This is setting the BASE scale from character customization
            // Update both base and current values
            humanoid.BaseWidth = scale.X;
            humanoid.BaseHeight = scale.Y;

            _appearance.SetScale(uid, scale, humanoid: humanoid);
            UpdateBagEligibility(uid, humanoid);
            return true;
        }

        return false;
    }

    private void UpdateBagEligibility(EntityUid uid, HumanoidAppearanceComponent humanoid)
    {
        // Pseudo items are not networked, so this state is managed authoritatively by the server.
        if (!_net.IsServer)
            return;

        var smallEnough = humanoid.Height < BagSizeThreshold && humanoid.Width < BagSizeThreshold;
        if (smallEnough)
        {
            // Do not overwrite a species' bespoke bag shape.
            if (HasComp<PseudoItemComponent>(uid))
                return;

            EnsureComp<PseudoItemComponent>(uid);
            EnsureComp<SmallHumanoidBagComponent>(uid);
            return;
        }

        // Only remove the pseudo-item if this system added it; species-defined pseudo-items retain their behavior.
        if (RemCompDeferred<SmallHumanoidBagComponent>(uid))
            RemCompDeferred<PseudoItemComponent>(uid);
    }
}
