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
    /// </summary>
    public static readonly CVarDef<bool> BloodTypesEnabled =
        CVarDef.Create("kritters.blood_types.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Colors the client pain overlay using the local character's current blood reagent.
    /// </summary>
    public static readonly CVarDef<bool> BloodColoredPainAura =
        CVarDef.Create("kritters.blood_types.blood_colored_pain_aura", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> AggressiveSpaceJanitorEnabled =
        CVarDef.Create("kritters.aggressive_space_janitor.enabled", true, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorScanIntervalSeconds =
        CVarDef.Create("kritters.aggressive_space_janitor.scan_interval_seconds", 60, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorLowValueLifetimeSeconds =
        CVarDef.Create("kritters.aggressive_space_janitor.low_value_lifetime_seconds", 1800, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorHighValueLifetimeSeconds =
        CVarDef.Create("kritters.aggressive_space_janitor.high_value_lifetime_seconds", 3600, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorGridLowValueLifetimeSeconds =
        CVarDef.Create("kritters.aggressive_space_janitor.grid_low_value_lifetime_seconds", 300, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorGridHighValueLifetimeSeconds =
        CVarDef.Create("kritters.aggressive_space_janitor.grid_high_value_lifetime_seconds", 900, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorHighValueThreshold =
        CVarDef.Create("kritters.aggressive_space_janitor.high_value_threshold", 500, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorMailLifetimeSeconds =
        CVarDef.Create("kritters.aggressive_space_janitor.mail_lifetime_seconds", 18000, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorPlayerRadius =
        CVarDef.Create("kritters.aggressive_space_janitor.player_radius", 10, CVar.SERVER | CVar.ARCHIVE);

    public static readonly CVarDef<int> AggressiveSpaceJanitorDeletionLimit =
        CVarDef.Create("kritters.aggressive_space_janitor.deletion_limit", 64, CVar.SERVER | CVar.ARCHIVE);
}
