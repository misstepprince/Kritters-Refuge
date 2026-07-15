using System.Linq;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._CS.HornyQuirks;

/// <summary>
/// This handles my balls
/// </summary>
public sealed partial class HornyExamineQuirksSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _rnd = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<HornyExamineQuirksComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, HornyExamineQuirksComponent component, ExaminedEvent args)
    {
        List<string> temperaments = new();
        var isSalf = args.Examiner == args.Examined;
        var targetIdent = ("target", Identity.Entity(args.Examined, EntityManager));
        var examinerIdent = ("examiner", Identity.Entity(args.Examiner, EntityManager));
        TryComp<HornyExamineQuirksComponent>(args.Examiner, out var examinerQuirks);
        using (args.PushGroup("DanIsCool"))
        {
            foreach (var showable in component.HornyShowables)
            {
                if (!_prototypeManager.TryIndex(showable, out var hornyProto))
                    continue;

                if (isSalf)
                {
                    if (hornyProto.SelfExamine)
                    {
                        goto ShowIt;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(hornyProto.NeededTag))
                {
                    goto ShowIt;
                }

                // check if examiner has the needed tag
                if (examinerQuirks is not null
                    && !examinerQuirks.HasTagToShow(hornyProto.NeededTag))
                {
                    continue;
                }

                ShowIt:
                temperaments.Add(hornyProto.TextToShow);
            }

            if (temperaments.Count > 0)
            {
                foreach (var loc in temperaments)
                {
                    var translated = Loc.GetString(
                        loc,
                        targetIdent,
                        examinerIdent);
                    args.PushMarkup(translated, 0);
                }
            }

            string bodyWords = formatulateBodyWords(uid, component);
            // stick body words at start
            if (!string.IsNullOrEmpty(bodyWords))
            {
                args.PushMarkup(bodyWords, 10);
            }
        }
    }

    /// <summary>
    /// Formatulate body words for horny quirks
    /// bodywords have two parts, descriptive and brief
    /// if its just one entry, just use the descriptive
    /// if its more than that, first one use descriptive, then rest use brief
    /// Supports up to infinity entries
    /// Example:
    /// Single entry: "She has a bountiful flowing erection for days."
    /// two entries: "She has a bountiful flowing erection for days, and broad shoulders."
    /// Three entries: "She has a bountiful flowing erection for days, broad shoulders, and a cupcake."
    /// Four entries: "She has a bountiful flowing erection for days, broad shoulders, a cupcake, and my dad."
    /// BUT WHICH ONE IS FIRST? its random, each time you examine!
    /// </summary>
    private string formatulateBodyWords(EntityUid horny, HornyExamineQuirksComponent component)
    {
        if (component.HornyAppearances.Count == 0)
            return string.Empty;

        var appearances = component.HornyAppearances.ToList();
        _rnd.Shuffle(appearances);
        // localize all entries in the appearances list
        var hormy = Identity.Entity(horny, EntityManager);
        for (int i = 0; i < appearances.Count; i++)
        {
            var descriptiveLoc = Loc.GetString(
                appearances[i].Item1,
                ("them", hormy));
            var briefLoc = Loc.GetString(
                appearances[i].Item2,
                ("them", hormy));
            descriptiveLoc = $"[color=forestgreen]{descriptiveLoc}[/color]";
            briefLoc =       $"[color=forestgreen]{briefLoc}[/color]";
            appearances[i] = (descriptiveLoc, briefLoc);
        }
        var descriptive = appearances[0].Item1;
        var briefList = appearances.Skip(1).Select(x => x.Item2).ToList();
        switch (briefList.Count)
        {
            case 0:
                return Loc.GetString(
                    "horny-examine-quirk-bodyword-single",
                    ("them", hormy),
                    ("first", descriptive));
            // case: two
            case 1:
                return Loc.GetString(
                    "horny-examine-quirk-bodyword-two",
                    ("them", hormy),
                    ("first", descriptive),
                    ("second", briefList[0]));
            default:
            {
                var last = briefList.Last();
                var middle = briefList.Take(briefList.Count - 1).ToList();
                return Loc.GetString(
                    "horny-examine-quirk-bodyword-multiple",
                    ("them", hormy),
                    ("first", descriptive),
                    ("second", string.Join(", ", middle)),
                    ("last", last));
            }
        }
    }
}
