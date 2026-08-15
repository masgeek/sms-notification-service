namespace FeeSyncer.Shared;

public static class FeeProcessorActivityLogger
{
    private static readonly Lock Sync = new();

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                var directory = ConfigPathResolver.GetLogDir();
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "fee-processor-updates.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [FeeProcessor] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Activity logging must never interrupt an update.
        }
    }
}
