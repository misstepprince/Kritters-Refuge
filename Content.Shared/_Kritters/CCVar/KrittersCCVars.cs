using Robust.Shared.Configuration;

namespace Content.Shared._Kritters.CCVar;

/// <summary>
/// CVars owned by Kritters content.
/// </summary>
[CVarDefs]
public sealed class KrittersCCVars
{
    /// <summary>
    /// Enables the Kritters character blood type selector and compatibility tag application.
    /// When disabled, species prototype blood remains authoritative.
    /// </summary>
    public static readonly CVarDef<bool> BloodTypesEnabled =
        CVarDef.Create("kritters.blood_types.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Colors the client pain overlay using the local character's current blood reagent.
    /// </summary>
    public static readonly CVarDef<bool> BloodColoredPainAura =
        CVarDef.Create("kritters.blood_types.blood_colored_pain_aura", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
