using System.Globalization;

namespace Content.Shared._Kritters.Novakin;

/// <summary>
/// Formats Novakin simulation values for player-facing text without changing their stored precision.
/// </summary>
public static class NovakinDisplayFormat
{
    public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
