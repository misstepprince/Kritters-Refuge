using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Humanoid.Markings
{
    public sealed partial class MarkingManager
    {
        [Dependency] private IPrototypeManager _prototypeManager = default!;

        private readonly List<MarkingPrototype> _index = new();
        public FrozenDictionary<MarkingCategories, FrozenDictionary<string, MarkingPrototype>> CategorizedMarkings = default!;
        public FrozenDictionary<string, MarkingPrototype> Markings = default!;

        public void Initialize()
        {
            _prototypeManager.PrototypesReloaded += OnPrototypeReload;
            CachePrototypes();
        }

        private void CachePrototypes()
        {
            _index.Clear();
            var markingDict = new Dictionary<MarkingCategories, Dictionary<string, MarkingPrototype>>();

            foreach (var category in Enum.GetValues<MarkingCategories>())
            {
                markingDict.Add(category, new());
            }

            foreach (var prototype in _prototypeManager.EnumeratePrototypes<MarkingPrototype>())
            {
                _index.Add(prototype);
                markingDict[prototype.MarkingCategory].Add(prototype.ID, prototype);
            }

            Markings = _prototypeManager.EnumeratePrototypes<MarkingPrototype>().ToFrozenDictionary(x => x.ID);
            CategorizedMarkings = markingDict.ToFrozenDictionary(
                x => x.Key,
                x => x.Value.ToFrozenDictionary());
        }

        public FrozenDictionary<string, MarkingPrototype> MarkingsByCategory(MarkingCategories category)
        {
            // all marking categories are guaranteed to have a dict entry
            return CategorizedMarkings[category];
        }

        /// <summary>
        ///     Markings by category and species.
        /// </summary>
        /// <param name="category"></param>
        /// <param name="species"></param>
        /// <remarks>
        ///     This is done per category, as enumerating over every single marking by species isn't useful.
        ///     Please make a pull request if you find a use case for that behavior.
        /// </remarks>
        /// <returns></returns>
        public IReadOnlyDictionary<string, MarkingPrototype> MarkingsByCategoryAndSpecies(MarkingCategories category,
            string species)
        {
            var speciesProto = _prototypeManager.Index<SpeciesPrototype>(species);
            var markingPoints = _prototypeManager.Index(speciesProto.MarkingPoints);
            var res = new Dictionary<string, MarkingPrototype>();

            foreach (var (key, marking) in MarkingsByCategory(category))
            {
                if (markingPoints.OnlyWhitelisted && marking.SpeciesRestrictions == null)
                {
                    continue;
                }
                if (markingPoints.Points.TryGetValue(category, out var value) &&
                    value.OnlyWhitelisted && marking.SpeciesRestrictions == null)
                {
                    continue;
                }

                if (!IsAllowedBySpeciesOrKindAllowance(speciesProto, marking))
                {
                    continue;
                }
                res.Add(key, marking);
            }

            return res;
        }

        /// <summary>
        ///     Markings by category and sex.
        /// </summary>
        /// <param name="category"></param>
        /// <param name="sex"></param>
        /// <remarks>
        ///     This is done per category, as enumerating over every single marking by species isn't useful.
        ///     Please make a pull request if you find a use case for that behavior.
        /// </remarks>
        /// <returns></returns>
        public IReadOnlyDictionary<string, MarkingPrototype> MarkingsByCategoryAndSex(MarkingCategories category,
            Sex sex)
        {
            var res = new Dictionary<string, MarkingPrototype>();

            foreach (var (key, marking) in MarkingsByCategory(category))
            {
                if (marking.SexRestriction != null && marking.SexRestriction != sex)
                {
                    continue;
                }

                res.Add(key, marking);
            }

            return res;
        }

        /// <summary>
        ///     Markings by category, species and sex.
        /// </summary>
        /// <param name="category"></param>
        /// <param name="species"></param>
        /// <param name="sex"></param>
        /// <remarks>
        ///     This is done per category, as enumerating over every single marking by species isn't useful.
        ///     Please make a pull request if you find a use case for that behavior.
        /// </remarks>
        /// <returns></returns>
        public IReadOnlyDictionary<string, MarkingPrototype> MarkingsByCategoryAndSpeciesAndSex(
            MarkingCategories category,
            string species,
            Sex sex
            )
        {
            var speciesProto = _prototypeManager.Index<SpeciesPrototype>(species);
            var onlyWhitelisted = _prototypeManager.Index(speciesProto.MarkingPoints).OnlyWhitelisted;
            var res = new Dictionary<string, MarkingPrototype>();

            foreach (var (key, marking) in MarkingsByCategory(category))
            {
                if (onlyWhitelisted && marking.SpeciesRestrictions == null)
                {
                    continue;
                }

                if (!IsAllowedBySpeciesOrKindAllowance(speciesProto, marking))
                {
                    continue;
                }

                if (marking.SexRestriction != null && marking.SexRestriction != sex)
                {
                    continue;
                }

                res.Add(key, marking);
            }

            return res;
        }

        public bool TryGetMarking(Marking marking, [NotNullWhen(true)] out MarkingPrototype? markingResult)
        {
            return Markings.TryGetValue(marking.MarkingId, out markingResult);
        }

        /// <summary>
        /// Is the marking allowed by the kind allowance of the marking prototype?
        /// </summary>
        public static bool IsAllowedBySpeciesOrKindAllowance(SpeciesPrototype speciesProto, MarkingPrototype marking)
        {
            if (marking.SpeciesRestrictions == null)
                return true; // no restrictions, so it's allowed
            if (marking.SpeciesRestrictions.Contains(speciesProto.ID))
                return true; // species is allowed
            // okay at this point, there is restrictions, and the species is not allowed.
            // check kinds!
            if (marking.KindAllowance == null)
                return false; // no kind allowance, so it's not allowed
            if (speciesProto.Kind == null)
                return false; // no kind, so it's not allowed
            if (marking.KindAllowance.Any(kind => speciesProto.Kind.Contains(kind)))
                return true; // kind is allowed, so it's allowed
            return false; // screw off
        }
        // overloaded version IsAllowedBySpeciesOrKindAllowance, in case its called with a string species ID
        private bool IsAllowedBySpeciesOrKindAllowance(string species, MarkingPrototype marking)
        {
            var speciesProto = _prototypeManager.Index<SpeciesPrototype>(species);
            return IsAllowedBySpeciesOrKindAllowance(speciesProto, marking);
        }
        // overloaded version IsAllowedBySpeciesOrKindAllowance, in case its called with a string marking ID
        public bool IsAllowedBySpeciesOrKindAllowance(SpeciesPrototype species, string markingId)
        {
            if (!Markings.TryGetValue(markingId, out var marking))
            {
                return false; // no such marking
            }
            return IsAllowedBySpeciesOrKindAllowance(species, marking);
        }
        // and just in case both are strings
        public bool IsAllowedBySpeciesOrKindAllowance(string species, string markingId)
        {
            var speciesProto = _prototypeManager.Index<SpeciesPrototype>(species);
            if (!Markings.TryGetValue(markingId, out var marking))
            {
                return false; // no such marking
            }
            return IsAllowedBySpeciesOrKindAllowance(speciesProto, marking);
        }

        /// <summary>
        ///     Check if a marking is valid according to the category, species, and current data this marking has.
        /// </summary>
        /// <param name="marking"></param>
        /// <param name="category"></param>
        /// <param name="species"></param>
        /// <param name="sex"></param>
        /// <returns></returns>
        public bool IsValidMarking(Marking marking, MarkingCategories category, string species, Sex sex)
        {
            if (!TryGetMarking(marking, out var proto))
            {
                return false;
            }

            if (proto.MarkingCategory != category)
            {
                return false;
            }
            if (proto.SpeciesRestrictions != null && !proto.SpeciesRestrictions.Contains(species))
            {
                var speciesProto = _prototypeManager.Index<SpeciesPrototype>(species);
                var isInvalid = true;
                if (proto.KindAllowance != null)
                {
                    if (speciesProto.Kind != null)
                    {
                        if (proto.KindAllowance.Any(kind => speciesProto.Kind.Contains(kind)))
                            isInvalid = false; // hi mom!
                    }
                }
                if (isInvalid)
                {
                    return false;
                }

            }
            if (proto.SexRestriction != null && proto.SexRestriction != sex)
            {
                return false;
            }

            if (marking.MarkingColors.Count != proto.Sprites.Count)
            {
                return false;
            }

            return true;
        }

        private void OnPrototypeReload(PrototypesReloadedEventArgs args)
        {
            if (args.WasModified<MarkingPrototype>())
                CachePrototypes();
        }

        public bool CanBeApplied(string species, Sex sex, Marking marking, IPrototypeManager? prototypeManager = null)
        {
            IoCManager.Resolve(ref prototypeManager);

            var speciesProto = prototypeManager.Index<SpeciesPrototype>(species);
            var onlyWhitelisted = prototypeManager.Index(speciesProto.MarkingPoints).OnlyWhitelisted;

            if (!TryGetMarking(marking, out var prototype))
            {
                return false;
            }

            if (onlyWhitelisted && prototype.SpeciesRestrictions == null)
            {
                return false;
            }

            if (prototype.SpeciesRestrictions != null
                && !prototype.SpeciesRestrictions.Contains(species))
            {
                return false;
            }

            if (prototype.SexRestriction != null && prototype.SexRestriction != sex)
            {
                return false;
            }

            return true;
        }

        public bool CanBeApplied(string species, Sex sex, MarkingPrototype prototype, IPrototypeManager? prototypeManager = null)
        {
            IoCManager.Resolve(ref prototypeManager);

            var speciesProto = prototypeManager.Index<SpeciesPrototype>(species);
            var onlyWhitelisted = prototypeManager.Index(speciesProto.MarkingPoints).OnlyWhitelisted;

            if (onlyWhitelisted && prototype.SpeciesRestrictions == null)
            {
                return false;
            }

            if (prototype.SpeciesRestrictions != null &&
                !prototype.SpeciesRestrictions.Contains(species))
            {
                return false;
            }

            if (prototype.SexRestriction != null && prototype.SexRestriction != sex)
            {
                return false;
            }

            return true;
        }

        public bool MustMatchSkin(string species, HumanoidVisualLayers layer, out float alpha, IPrototypeManager? prototypeManager = null)
        {
            IoCManager.Resolve(ref prototypeManager);
            var speciesProto = prototypeManager.Index<SpeciesPrototype>(species);
            if (!prototypeManager.TryIndex(speciesProto.SpriteSet, out var baseSprites)
                || !baseSprites.Sprites.TryGetValue(layer, out var spriteName)
                || !prototypeManager.TryIndex(spriteName, out HumanoidSpeciesSpriteLayer? sprite)
                || !sprite.MarkingsMatchSkin
            )
            {
                alpha = 1f;
                return false;
            }

            alpha = sprite.LayerAlpha;
            return true;
        }

        // Frontier: allow markings to force a specific color
        public Color? MustMatchColor(string species, HumanoidVisualLayers layer, out float alpha, IPrototypeManager? prototypeManager = null)
        {
            IoCManager.Resolve(ref prototypeManager);
            var speciesProto = prototypeManager.Index<SpeciesPrototype>(species);
            if (!prototypeManager.TryIndex(speciesProto.SpriteSet, out HumanoidSpeciesBaseSpritesPrototype? baseSprites)
                || !baseSprites.Sprites.TryGetValue(layer, out var spriteName)
                || !prototypeManager.TryIndex(spriteName, out HumanoidSpeciesSpriteLayer? sprite)
                || !sprite.ForcedColoring
            )
            {
                alpha = 1f;
                return null;
            }

            alpha = sprite.LayerAlpha;
            return speciesProto.ForcedMarkingColor;
        }
        // End Frontier
    }
}
