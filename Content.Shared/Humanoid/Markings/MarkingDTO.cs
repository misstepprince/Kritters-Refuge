using System.Linq;
using System.Text.Json.Serialization;
using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid.Markings
{
    public sealed class MarkingDTO
    {
        public List<string> MarkingColors { get; set; } = new() { Color.White.ToHex() };

        public MarkingDTO()
        {
        }

        /// <summary>
        ///     ID of the marking prototype.
        /// </summary>
        public string? MarkingId { get; set; }

        /// <summary>
        ///     If this marking is currently visible.
        public bool? Visible { get; set; }

        /// <summary>
        ///     If this marking is can be toggled on or off by the user.
        /// </summary>
        public string? CustomName { get; set; }

        /// <summary>
        ///     If this marking is can be toggled on or off by the user.
        /// </summary>
        public bool? CanToggleVisible { get; set; }

        /// <summary>
        ///     If this marking is can be toggled on or off by the other players.
        /// </summary>
        public bool? OtherCanToggleVisible { get; set; }

        /// <summary>
        ///     Whether toggle popup text should be suppressed in game.
        /// </summary>
        public bool? HideTogglePopup { get; set; }

        /// <summary>
        ///     Verb to use when putting on
        /// </summary>
        public string? PutOnVerb { get; set; }

        /// <summary>
        ///     Verb to use when taking off
        /// </summary>
        public string? TakeOffVerb { get; set; }

        /// <summary>
        ///     Verb to use when putting on (2nd person)
        /// </summary>
        public string? PutOnVerb2p { get; set; }

        /// <summary>
        ///     Verb to use when taking off (2nd person)
        /// </summary>
        public string? TakeOffVerb2p { get; set; }

        /// <summary>
        ///     If this marking should render over clothing when possible.
        /// </summary>
        public bool? RenderOverClothing { get; set; }

        /// <summary>
        ///     Per-color glow intensities from 0 to 1.
        /// </summary>
        public List<float>? GlowLevels { get; set; }

        /// <summary>
        ///     Per-marking glow intensity from 0 to 1.
        /// </summary>
        public float? Glow { get; set; }

        // Coyote Start
        /// <summary>
        ///     Uniform scale multiplier for the marking sprite. Defaults to 1.0.
        /// </summary>
        public float? Scale { get; set; }

        /// <summary>
        ///     Marking offset X in local sprite coordinates.
        /// </summary>
        public float? OffsetX { get; set; }

        /// <summary>
        ///     Marking offset Y in local sprite coordinates.
        /// </summary>
        public float? OffsetY { get; set; }

        /// <summary>
        ///     Marking offset X when facing front (south).
        /// </summary>
        public float? OffsetFrontX { get; set; }

        /// <summary>
        ///     Marking offset Y when facing front (south).
        /// </summary>
        public float? OffsetFrontY { get; set; }

        /// <summary>
        ///     Marking offset X when facing behind (north).
        /// </summary>
        public float? OffsetBehindX { get; set; }

        /// <summary>
        ///     Marking offset Y when facing behind (north).
        /// </summary>
        public float? OffsetBehindY { get; set; }

        /// <summary>
        ///     Marking offset X when facing left (west).
        /// </summary>
        public float? OffsetLeftX { get; set; }

        /// <summary>
        ///     Marking offset Y when facing left (west).
        /// </summary>
        public float? OffsetLeftY { get; set; }

        /// <summary>
        ///     Marking offset X when facing right (east).
        /// </summary>
        public float? OffsetRightX { get; set; }

        /// <summary>
        ///     Marking offset Y when facing right (east).
        /// </summary>
        public float? OffsetRightY { get; set; }
        // Coyote End
    }
}
