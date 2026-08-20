using System.Text.Json;

namespace FeeSyncer.Shared;

public static class UpdateCheckSchedule
{
    public const string DefaultValue = "4h";

    public static TimeSpan DefaultInterval => TimeSpan.FromHours(4);

    public static string Normalize(string? value) =>
        FeeProcessorInterval.Normalize(value, DefaultValue);

    public static TimeSpan ParseOrDefault(string? value) =>
        FeeProcessorInterval.TryParse(value, out var interval) ? interval : DefaultInterval;

    public static string LoadConfiguredValue()
    {
        try
        {
            var configPath = ConfigPathResolver.FindConfigFile();
            if (!File.Exists(configPath))
                return DefaultValue;

            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("Tray", out var tray) &&
                tray.TryGetProperty("UpdateCheckInterval", out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return Normalize(value.GetString());
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warn("Updater", $"Could not read update check interval: {exception.Message}");
        }

        return DefaultValue;
    }
}
