using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid.Markings
{
    [DataDefinition]
    [Serializable, NetSerializable]
    public sealed partial class Marking : IEquatable<Marking>, IComparable<Marking>, IComparable<string>
    {
        [DataField("markingColor")]
        private List<Color> _markingColors = new();

        [DataField("glowLevels")]
        private List<float> _markingGlow = new();

        [DataField("glow")]
        private float _legacyGlow;

    // Coyote Start
    [DataField("scale")]
    private float _markingScale = 1.0f;

    [DataField("offsetX")]
    private float _markingOffsetX;

    [DataField("offsetY")]
    private float _markingOffsetY;

    [DataField("offsetFrontX")]
    private float _markingOffsetFrontX;

    [DataField("offsetFrontY")]
    private float _markingOffsetFrontY;

    [DataField("offsetBehindX")]
    private float _markingOffsetBehindX;

    [DataField("offsetBehindY")]
    private float _markingOffsetBehindY;

    [DataField("offsetLeftX")]
    private float _markingOffsetLeftX;

    [DataField("offsetLeftY")]
    private float _markingOffsetLeftY;

    [DataField("offsetRightX")]
    private float _markingOffsetRightX;

    [DataField("offsetRightY")]
    private float _markingOffsetRightY;
    // Coyote End

        private Marking()
        {
        }

        public Marking(string markingId,
            List<Color> markingColors)
        {
            MarkingId = markingId;
            _markingColors = markingColors;
            _markingGlow = CreateGlowLevels(markingColors.Count);
        }

        public Marking(string markingId,
            List<Color> markingColors, MarkingCategories category) : this(markingId, markingColors.Count, category)
        {
            MarkingId = markingId;
            _markingColors = markingColors;
        }

        public Marking(Marking marking,
            List<Color> markingColors) : this(marking)
        {
            _markingColors = markingColors;
            _markingGlow = NormalizeGlowLevels(marking.MarkingGlow, markingColors.Count, marking._legacyGlow);
        }

        public Marking(string markingId,
            IReadOnlyList<Color> markingColors)
            : this(markingId, new List<Color>(markingColors))
        {
        }

        public Marking(Marking marking,
            IReadOnlyList<Color> markingColors)
            : this(marking)
        {
            _markingColors = new(markingColors);
            _markingGlow = NormalizeGlowLevels(marking.MarkingGlow, _markingColors.Count, marking._legacyGlow);
        }

        /// <summary>
        /// Creates a new marking from metadata, setting defaults based on category
        /// </summary>
        public Marking(string markingId, int colorCount, MarkingCategories category)
        {
            MarkingId = markingId;
            List<Color> colors = new();
            for (int i = 0; i < colorCount; i++)
                colors.Add(Color.White);
            _markingColors = colors;
            _markingGlow = CreateGlowLevels(colorCount);

            if (category == MarkingCategories.UndergarmentBottom || category == MarkingCategories.UndergarmentTop)
            {
                CanToggleVisible = true;
                OtherCanToggleVisible = true;
            }
            else if (category == MarkingCategories.Genital)
            {
                ShowAtStart = false;
                CanToggleVisible = true;
                OtherCanToggleVisible = false;
                PutOnVerb = "show";
                PutOnVerb2p = "shows";
                TakeOffVerb = "hide";
                TakeOffVerb2p = "hides";
            }
            else
            {
                CanToggleVisible = false;
                OtherCanToggleVisible = false;
                PutOnVerb = "show";
                PutOnVerb2p = "shows";
                TakeOffVerb = "hide";
                TakeOffVerb2p = "hides";
            }
        }

        public Marking(Marking marking, int colorCount) : this(marking)
        {
            List<Color> colors = new();
            for (int i = 0; i < colorCount; i++)
                colors.Add(Color.White);
            _markingColors = colors;
            _markingGlow = NormalizeGlowLevels(marking.MarkingGlow, colorCount, marking._legacyGlow);
        }

        public Marking(Marking other)
        {
            MarkingId = other.MarkingId;
            _markingColors = new(other.MarkingColors);
            Visible = other.Visible;
            Forced = other.Forced;
            CustomName = other.CustomName;
            CanToggleVisible = other.CanToggleVisible;
            OtherCanToggleVisible = other.OtherCanToggleVisible;
            HideTogglePopup = other.HideTogglePopup;
            PutOnVerb = other.PutOnVerb;
            PutOnVerb2p = other.PutOnVerb2p;
            TakeOffVerb = other.TakeOffVerb;
            TakeOffVerb2p = other.TakeOffVerb2p;
            ShowAtStart = other.ShowAtStart;
            RenderOverClothing = other.RenderOverClothing;
            _markingGlow = new(other.MarkingGlow);
            _legacyGlow = other._legacyGlow;
            _markingScale = other._markingScale; // Coyote
            _markingOffsetX = other._markingOffsetX; // Coyote
            _markingOffsetY = other._markingOffsetY; // Coyote
            _markingOffsetFrontX = other._markingOffsetFrontX; // Coyote
            _markingOffsetFrontY = other._markingOffsetFrontY; // Coyote
            _markingOffsetBehindX = other._markingOffsetBehindX; // Coyote
            _markingOffsetBehindY = other._markingOffsetBehindY; // Coyote
            _markingOffsetLeftX = other._markingOffsetLeftX; // Coyote
            _markingOffsetLeftY = other._markingOffsetLeftY; // Coyote
            _markingOffsetRightX = other._markingOffsetRightX; // Coyote
            _markingOffsetRightY = other._markingOffsetRightY; // Coyote
        }

        public Marking(MarkingDTO? other)
        {
            if (other == null) return;
            MarkingId = other.MarkingId ?? MarkingId;
            _markingColors = new(other.MarkingColors.Select(x => Color.FromHex(x)) ?? _markingColors);
            ShowAtStart = other.Visible ?? ShowAtStart;
            CustomName = other.CustomName ?? CustomName;
            CanToggleVisible = other.CanToggleVisible ?? CanToggleVisible;
            OtherCanToggleVisible = other.OtherCanToggleVisible ?? OtherCanToggleVisible;
            HideTogglePopup = other.HideTogglePopup ?? HideTogglePopup;
            PutOnVerb = other.PutOnVerb ?? PutOnVerb;
            PutOnVerb2p = other.PutOnVerb2p ?? PutOnVerb2p;
            TakeOffVerb = other.TakeOffVerb ?? TakeOffVerb;
            TakeOffVerb2p = other.TakeOffVerb2p ?? TakeOffVerb2p;
            RenderOverClothing = other.RenderOverClothing ?? RenderOverClothing;
            _markingGlow = NormalizeGlowLevels(other.GlowLevels, _markingColors.Count, other.Glow ?? 0f);
            _legacyGlow = other.Glow ?? 0f;
            _markingScale = Math.Clamp(other.Scale ?? 1.0f, 0.1f, 4.0f); // Coyote
            _markingOffsetX = Math.Clamp(other.OffsetX ?? 0f, -2f, 2f); // Coyote
            _markingOffsetY = Math.Clamp(other.OffsetY ?? 0f, -2f, 2f); // Coyote
            _markingOffsetFrontX = Math.Clamp(other.OffsetFrontX ?? _markingOffsetX, -2f, 2f); // Coyote
            _markingOffsetFrontY = Math.Clamp(other.OffsetFrontY ?? _markingOffsetY, -2f, 2f); // Coyote
            _markingOffsetBehindX = Math.Clamp(other.OffsetBehindX ?? _markingOffsetX, -2f, 2f); // Coyote
            _markingOffsetBehindY = Math.Clamp(other.OffsetBehindY ?? _markingOffsetY, -2f, 2f); // Coyote
            _markingOffsetLeftX = Math.Clamp(other.OffsetLeftX ?? _markingOffsetX, -2f, 2f); // Coyote
            _markingOffsetLeftY = Math.Clamp(other.OffsetLeftY ?? _markingOffsetY, -2f, 2f); // Coyote
            _markingOffsetRightX = Math.Clamp(other.OffsetRightX ?? _markingOffsetX, -2f, 2f); // Coyote
            _markingOffsetRightY = Math.Clamp(other.OffsetRightY ?? _markingOffsetY, -2f, 2f); // Coyote
        }

        /// <summary>
        ///     ID of the marking prototype.
        /// </summary>
        [DataField("markingId", required: true)]
        public string MarkingId { get; private set; } = default!;

        /// <summary>
        ///     All colors currently on this marking.
        /// </summary>
        [ViewVariables]
        public IReadOnlyList<Color> MarkingColors => _markingColors;

        [ViewVariables]
        public IReadOnlyList<float> MarkingGlow => _markingGlow;

    // Coyote Start
    public float MarkingScale => _markingScale;
    public Vector2 MarkingOffset => new(_markingOffsetX, _markingOffsetY);

    public Vector2 MarkingOffsetFront => new(_markingOffsetFrontX, _markingOffsetFrontY);
    public Vector2 MarkingOffsetBehind => new(_markingOffsetBehindX, _markingOffsetBehindY);
    public Vector2 MarkingOffsetLeft => new(_markingOffsetLeftX, _markingOffsetLeftY);
    public Vector2 MarkingOffsetRight => new(_markingOffsetRightX, _markingOffsetRightY);
    // Coyote End

        /// <summary>
        ///     If this marking is currently visible.
        /// </summary>
        [DataField("visible")]
        public bool Visible = true;

        /// <summary>
        ///     If this marking is can be toggled on or off by the user.
        /// </summary>
        [DataField("customName")]
        public string? CustomName = null;

        /// <summary>
        ///     If this marking is should start enabled.
        /// </summary>
        [DataField("showAtStart")]
        public bool ShowAtStart = true;

        /// <summary>
        ///     If true, this marking should render over worn clothing when supported.
        /// </summary>
        [DataField("renderOverClothing")]
        public bool RenderOverClothing = false;

        /// <summary>
        ///     If this marking is can be toggled on or off by the user.
        /// </summary>
        [DataField("canToggleVisible")]
        public bool CanToggleVisible = false;

        /// <summary>
        ///     If this marking is can be toggled on or off by the other players.
        /// </summary>
        [DataField("otherCanToggleVisible")]
        public bool OtherCanToggleVisible = false;

        /// <summary>
        ///     If toggle popup text should be suppressed when this marking is toggled.
        /// </summary>
        [DataField("hideTogglePopup")]
        public bool HideTogglePopup = false;

        /// <summary>
        ///     Verb to use when putting on
        /// </summary>
        [DataField("putOnVerb")]
        public string PutOnVerb = "put on";

        /// <summary>
        ///     Verb to use when taking off
        /// </summary>
        [DataField("takeOffVerb")]
        public string TakeOffVerb = "take off";

        /// <summary>
        ///     Verb to use when putting on (2nd person)
        /// </summary>
        [DataField("putOnVerb2p")]
        public string PutOnVerb2p = "puts on";

        /// <summary>
        ///     Verb to use when taking off (2nd person)
        /// </summary>
        [DataField("takeOffVerb2p")]
        public string TakeOffVerb2p = "takes off";

        /// <summary>
        ///     If this marking should be forcefully applied, regardless of points.
        /// </summary>
        [ViewVariables]
        public bool Forced;

        public void SetColor(int colorIndex, Color color) =>
            _markingColors[colorIndex] = color;

        public void SetGlow(int glowIndex, float glow)
        {
            if (glowIndex < 0 || glowIndex >= _markingGlow.Count)
                return;

            var normalizedGlow = Math.Clamp(glow, 0f, 1f);
            _markingGlow[glowIndex] = normalizedGlow;
            _legacyGlow = normalizedGlow;
        }

        // Coyote Start
        public void SetScale(float scale)
        {
            _markingScale = Math.Clamp(scale, 0.1f, 4.0f);
        }

        public void SetOffset(float x, float y)
        {
            _markingOffsetX = Math.Clamp(x, -2f, 2f);
            _markingOffsetY = Math.Clamp(y, -2f, 2f);

            // Keep directional offsets aligned with legacy single-offset behavior
            // when this setter is used.
            _markingOffsetFrontX = _markingOffsetX;
            _markingOffsetFrontY = _markingOffsetY;
            _markingOffsetBehindX = _markingOffsetX;
            _markingOffsetBehindY = _markingOffsetY;
            _markingOffsetLeftX = _markingOffsetX;
            _markingOffsetLeftY = _markingOffsetY;
            _markingOffsetRightX = _markingOffsetX;
            _markingOffsetRightY = _markingOffsetY;
        }

        public void SetOffset(Direction direction, float x, float y)
        {
            var clampedX = Math.Clamp(x, -2f, 2f);
            var clampedY = Math.Clamp(y, -2f, 2f);

            switch (direction)
            {
                case Direction.South:
                    _markingOffsetFrontX = clampedX;
                    _markingOffsetFrontY = clampedY;
                    break;
                case Direction.North:
                    _markingOffsetBehindX = clampedX;
                    _markingOffsetBehindY = clampedY;
                    break;
                case Direction.West:
                    _markingOffsetLeftX = clampedX;
                    _markingOffsetLeftY = clampedY;
                    break;
                case Direction.East:
                    _markingOffsetRightX = clampedX;
                    _markingOffsetRightY = clampedY;
                    break;
                default:
                    _markingOffsetFrontX = clampedX;
                    _markingOffsetFrontY = clampedY;
                    break;
            }
        }

        public Vector2 GetOffset(Direction direction)
        {
            return direction switch
            {
                Direction.South => MarkingOffsetFront,
                Direction.North => MarkingOffsetBehind,
                Direction.West => MarkingOffsetLeft,
                Direction.East => MarkingOffsetRight,
                _ => MarkingOffsetFront,
            };
        }
        // Coyote End

        public void SetColor(Color color)
        {
            for (int i = 0; i < _markingColors.Count; i++)
            {
                _markingColors[i] = color;
            }
        }

        public int CompareTo(Marking? marking)
        {
            if (marking == null)
            {
                return 1;
            }

            return string.Compare(MarkingId, marking.MarkingId, StringComparison.Ordinal);
        }

        public int CompareTo(string? markingId)
        {
            if (markingId == null)
                return 1;

            return string.Compare(MarkingId, markingId, StringComparison.Ordinal);
        }

        public bool Equals(Marking? other)
        {
            if (other == null)
            {
                return false;
            }
            return MarkingId.Equals(other.MarkingId)
                && _markingColors.SequenceEqual(other._markingColors)
                && Visible.Equals(other.Visible)
                && Forced.Equals(other.Forced)
                && CustomName == other.CustomName
                && CanToggleVisible == other.CanToggleVisible
                && OtherCanToggleVisible == other.OtherCanToggleVisible
                && HideTogglePopup == other.HideTogglePopup
                && PutOnVerb == other.PutOnVerb
                && PutOnVerb2p == other.PutOnVerb2p
                && TakeOffVerb == other.TakeOffVerb
                && TakeOffVerb2p == other.TakeOffVerb2p
                && ShowAtStart == other.ShowAtStart
                && RenderOverClothing == other.RenderOverClothing
                && _markingGlow.SequenceEqual(other._markingGlow)
                && _markingScale == other._markingScale // Coyote
                && _markingOffsetX == other._markingOffsetX // Coyote
                && _markingOffsetY == other._markingOffsetY // Coyote
                && _markingOffsetFrontX == other._markingOffsetFrontX // Coyote
                && _markingOffsetFrontY == other._markingOffsetFrontY // Coyote
                && _markingOffsetBehindX == other._markingOffsetBehindX // Coyote
                && _markingOffsetBehindY == other._markingOffsetBehindY // Coyote
                && _markingOffsetLeftX == other._markingOffsetLeftX // Coyote
                && _markingOffsetLeftY == other._markingOffsetLeftY // Coyote
                && _markingOffsetRightX == other._markingOffsetRightX // Coyote
                && _markingOffsetRightY == other._markingOffsetRightY; // Coyote
        }

        public MarkingDTO ToDTO()
        {
            return new MarkingDTO()
            {
                MarkingId = MarkingId,
                CanToggleVisible = CanToggleVisible,
                CustomName = CustomName,
                MarkingColors = _markingColors.Select(x => x.ToHex()).ToList(),
                Visible = ShowAtStart,
                OtherCanToggleVisible = OtherCanToggleVisible,
                HideTogglePopup = HideTogglePopup,
                PutOnVerb = PutOnVerb,
                PutOnVerb2p = PutOnVerb2p,
                TakeOffVerb = TakeOffVerb,
                TakeOffVerb2p = TakeOffVerb2p,
                RenderOverClothing = RenderOverClothing,
                GlowLevels = _markingGlow.ToList(),
                Glow = _markingGlow.FirstOrDefault(),
                Scale = _markingScale, // Coyote
                OffsetX = _markingOffsetX, // Coyote
                OffsetY = _markingOffsetY, // Coyote
                OffsetFrontX = _markingOffsetFrontX, // Coyote
                OffsetFrontY = _markingOffsetFrontY, // Coyote
                OffsetBehindX = _markingOffsetBehindX, // Coyote
                OffsetBehindY = _markingOffsetBehindY, // Coyote
                OffsetLeftX = _markingOffsetLeftX, // Coyote
                OffsetLeftY = _markingOffsetLeftY, // Coyote
                OffsetRightX = _markingOffsetRightX, // Coyote
                OffsetRightY = _markingOffsetRightY, // Coyote
            };
        }

        private static List<float> CreateGlowLevels(int count)
        {
            List<float> glowLevels = new();
            for (var i = 0; i < count; i++)
            {
                glowLevels.Add(0f);
            }

            return glowLevels;
        }

        private static List<float> NormalizeGlowLevels(IEnumerable<float>? source, int count, float fallback)
        {
            var normalizedFallback = Math.Clamp(fallback, 0f, 1f);
            var sourceList = source?.Select(value => Math.Clamp(value, 0f, 1f)).ToList() ?? new List<float>();

            if (sourceList.Count > count)
                sourceList.RemoveRange(count, sourceList.Count - count);

            while (sourceList.Count < count)
            {
                sourceList.Add(normalizedFallback);
            }

            return sourceList;
        }
    }
}
