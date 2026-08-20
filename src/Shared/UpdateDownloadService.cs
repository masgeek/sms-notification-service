using System.Buffers;
using System.Security.Cryptography;

namespace FeeSyncer.Shared;

public sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public int Percentage => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesReceived * 100 / TotalBytes, 0, 100);
}

public sealed class VerifiedUpdateDownload : IDisposable
{
    private readonly FileStream _lease;

    internal VerifiedUpdateDownload(string path, FileStream lease)
    {
        Path = path;
        _lease = lease;
    }

    public string Path { get; }

    public void Dispose() => _lease.Dispose();
}

public sealed class UpdateDownloadService
{
    private const long MaximumInstallerSize = 1024L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _updatesDirectory;

    public UpdateDownloadService()
        : this(CreateHttpClient(), Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Munywele",
            "FeeSyncer",
            "Updates"))
    {
    }

    public UpdateDownloadService(HttpClient httpClient, string updatesDirectory)
    {
        _httpClient = httpClient;
        _updatesDirectory = updatesDirectory;
    }

    public async Task<VerifiedUpdateDownload> DownloadAsync(
        UpdateCheckResult update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateMetadata(update);
        var versionDirectory = Path.Combine(
            _updatesDirectory,
            update.LatestVersion!,
            Guid.NewGuid().ToString("N"));
        var fileName = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        var installerPath = Path.Combine(versionDirectory, fileName);
        FileStream? output = null;

        Directory.CreateDirectory(versionDirectory);

        try
        {
            using var response = await GetInstallerResponseAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();

            var expectedSize = update.Size!.Value;
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != expectedSize)
                throw new InvalidDataException($"Installer size mismatch. Expected {expectedSize} bytes but the server reported {contentLength}.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            output = new FileStream(
                installerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long received = 0;

            try
            {
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    received += read;
                    if (received > expectedSize)
                        throw new InvalidDataException("The downloaded installer exceeded its declared size.");

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new UpdateDownloadProgress(received, expectedSize));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await output.FlushAsync(cancellationToken);
            if (received != expectedSize)
                throw new InvalidDataException($"Installer size mismatch. Expected {expectedSize} bytes but downloaded {received}.");

            var expectedHash = Convert.FromHexString(update.Sha256!);
            var actualHash = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");

            await output.DisposeAsync();
            output = null;

            var lease = new FileStream(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            try
            {
                var leasedHash = SHA256.HashData(lease);
                if (!CryptographicOperations.FixedTimeEquals(leasedHash, expectedHash))
                    throw new InvalidDataException("The downloaded installer changed before it could be secured for launch.");

                lease.Position = 0;
                progress?.Report(new UpdateDownloadProgress(expectedSize, expectedSize));
                return new VerifiedUpdateDownload(installerPath, lease);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
        catch
        {
            if (output is not null)
                await output.DisposeAsync();
            TryDelete(installerPath);
            TryDeleteDirectory(versionDirectory);
            throw;
        }
    }

    private static Uri ValidateMetadata(UpdateCheckResult update)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrWhiteSpace(update.LatestVersion))
            throw new InvalidOperationException("No newer release is available for installation.");
        if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var uri) ||
            !TrustedUpdateSource.IsTrustedInstaller(uri, update.LatestVersion))
        {
            throw new InvalidOperationException("The installer URL is not trusted.");
        }
        if (update.Size is null or <= 0 or > MaximumInstallerSize)
            throw new InvalidOperationException("The installer size is invalid.");

        try
        {
            if (string.IsNullOrWhiteSpace(update.Sha256) || Convert.FromHexString(update.Sha256).Length != 32)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("The installer SHA-256 checksum is invalid.");
        }

        return uri;
    }

    private async Task<HttpResponseMessage> GetInstallerResponseAsync(Uri installerUri, CancellationToken cancellationToken)
    {
        var requestUri = installerUri;
        for (var redirectCount = 0; redirectCount <= 3; redirectCount++)
        {
            var response = await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is < 300 or >= 400)
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new InvalidDataException("The installer download returned a redirect without a destination.");

            var redirectUri = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
            if (!string.Equals(installerUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !TrustedUpdateSource.IsTrustedGitHubAssetRedirect(redirectUri))
            {
                throw new InvalidDataException("The installer download redirected to an untrusted destination.");
            }

            requestUri = redirectUri;
        }

        throw new InvalidDataException("The installer download exceeded the redirect limit.");
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
