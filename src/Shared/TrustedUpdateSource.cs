namespace FeeSyncer.Shared;

internal static class TrustedUpdateSource
{
    private const string GitHubReleasePath = "/masgeek/sms-notification-service/releases/download/";

    public static bool IsTrustedInstaller(Uri uri, string version)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(uri.Host, "s3.munywele.co.ke", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.StartsWith($"/fee-syncer/{version}/", StringComparison.Ordinal);

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(GitHubReleasePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = uri.AbsolutePath[GitHubReleasePath.Length..];
        var separator = remainder.IndexOf('/');
        if (separator <= 0)
            return false;

        var tag = remainder[..separator];
        return string.Equals(tag, version, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tag, $"v{version}", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrustedGitHubAssetRedirect(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
}
