using System.Globalization;
using System.Text.RegularExpressions;

namespace FeeSyncer.Shared;

public static partial class FeeProcessorInterval
{
    private static readonly TimeSpan Maximum = TimeSpan.FromDays(365);

    public static bool TryParse(string? value, out TimeSpan interval)
    {
        interval = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
        {
            interval = TimeSpan.FromHours(hours);
            return IsValid(interval);
        }

        var match = DurationTokenRegex().Match(text);
        if (!match.Success)
            return false;

        var amount = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        interval = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            "d" => TimeSpan.FromDays(amount),
            _ => TimeSpan.Zero
        };
        return IsValid(interval);
    }

    public static string Normalize(string? value, string fallback = "24h") =>
        TryParse(value, out var interval) ? Format(interval) : fallback;

    public static string Format(TimeSpan interval)
    {
        if (interval.TotalDays >= 1 && interval.TotalDays % 1 == 0) return $"{interval.TotalDays:0}d";
        if (interval.TotalHours >= 1 && interval.TotalHours % 1 == 0) return $"{interval.TotalHours:0}h";
        if (interval.TotalMinutes >= 1 && interval.TotalMinutes % 1 == 0) return $"{interval.TotalMinutes:0}m";
        return $"{interval.TotalSeconds:0}s";
    }

    private static bool IsValid(TimeSpan interval) => interval > TimeSpan.Zero && interval <= Maximum;

    [GeneratedRegex(@"^\s*([0-9]+)\s*([mhd])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex();
}
