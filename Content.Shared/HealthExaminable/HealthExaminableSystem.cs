using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.HealthExaminable;

public sealed partial class HealthExaminableSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examineSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HealthExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
        SubscribeLocalEvent<HealthExaminableComponent, ExaminedEvent>(HandleExamined);
    }

    private void OnGetExamineVerbs(EntityUid uid, HealthExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return;

        var detailsRange = _examineSystem.IsInDetailsRange(args.User, uid);

        var verb = new ExamineVerb()
        {
            Act = () =>
            {
                var markup = CreateMarkup(uid, component, damage);
                _examineSystem.SendExamineTooltip(args.User, uid, markup, false, false);
                var examineCompletedEvent = new ExamineCompletedEvent(markup, uid, args.User, true); // Goobstation
                RaiseLocalEvent(uid, examineCompletedEvent); // Goobstation
            },
            Text = Loc.GetString("health-examinable-verb-text"),
            Category = VerbCategory.ExamineGroup,
            Disabled = !detailsRange,
            Message = detailsRange ? null : Loc.GetString("health-examinable-verb-disabled"),
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png"))
        };

        args.Verbs.Add(verb);
    }

    public FormattedMessage CreateMarkup(EntityUid uid,
        HealthExaminableComponent component,
        DamageableComponent damage,
        // Floof: empty message for basic examine
        bool showFallbackMessage = true)
    {
        var msg = new FormattedMessage();

        var first = true;
        foreach (var type in component.ExaminableTypes)
        {
            if (!damage.Damage.DamageDict.TryGetValue(type, out var dmg))
                continue;

            if (dmg == FixedPoint2.Zero)
                continue;

            FixedPoint2 closest = FixedPoint2.Zero;

            string chosenLocStr = string.Empty;
            foreach (var threshold in component.Thresholds)
            {
                var str = $"health-examinable-{component.LocPrefix}-{type}-{threshold}";
                var tempLocStr = Loc.GetString($"health-examinable-{component.LocPrefix}-{type}-{threshold}", ("target", Identity.Entity(uid, EntityManager)));

                // i.e., this string doesn't exist, because theres nothing for that threshold
                if (tempLocStr == str)
                    continue;

                if (dmg > threshold && threshold > closest)
                {
                    chosenLocStr = tempLocStr;
                    closest = threshold;
                }
            }

            if (closest == FixedPoint2.Zero)
                continue;

            if (!first)
            {
                msg.PushNewline();
            }
            else
            {
                first = false;
            }
            msg.AddMarkupOrThrow(chosenLocStr);
        }

        // Floof: empty message for basic examine
        if (msg.IsEmpty && showFallbackMessage)
        {
            msg.AddMarkupOrThrow(Loc.GetString($"health-examinable-{component.LocPrefix}-none"));
        }

        // Anything else want to add on to this?
        RaiseLocalEvent(uid, new HealthBeingExaminedEvent(msg), true);

        return msg;
    }

    // Floof: basic examine
    private void HandleExamined(EntityUid examinedUid, HealthExaminableComponent component, ExaminedEvent args)
    {
        if (!TryComp(examinedUid, out DamageableComponent? damage))
            return;

        using (args.PushGroup(nameof(HealthExaminableComponent)))
        {
            // only show the default health inspect, leave self-aware to the actual health examine action
            args.PushMessage(CreateMarkup(examinedUid, component, damage, false));
        }
    }
}

/// <summary>
///     A class raised on an entity whose health is being examined
///     in order to add special text that is not handled by the
///     damage thresholds.
/// </summary>
public sealed partial class HealthBeingExaminedEvent
{
    public FormattedMessage Message;

    public HealthBeingExaminedEvent(FormattedMessage message)
    {
        Message = message;
    }
}
