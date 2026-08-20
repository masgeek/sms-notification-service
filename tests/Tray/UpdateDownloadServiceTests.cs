using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using FeeSyncer.Shared;
using FluentAssertions;

namespace FeeSyncer.Tray.Tests;

public sealed class UpdateDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_ValidInstaller_VerifiesAndMovesCompletedFile()
    {
        var bytes = "verified installer"u8.ToArray();
        var root = CreateTemporaryDirectory();
        var progress = new List<UpdateDownloadProgress>();
        using var http = CreateHttpClient(bytes);
        var service = new UpdateDownloadService(http, root);

        try
        {
            using var download = await service.DownloadAsync(
                CreateUpdate(bytes),
                new InlineProgress<UpdateDownloadProgress>(progress.Add));

            File.Exists(download.Path).Should().BeTrue();
            using (var read = File.OpenRead(download.Path))
                (await ReadAllBytesAsync(read)).Should().Equal(bytes);
            Action replace = () =>
            {
                using var stream = File.Open(download.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            };
            replace.Should().Throw<IOException>("the verified installer remains leased until elevation starts");
            progress.Should().NotBeEmpty();
            progress[^1].Percentage.Should().Be(100);
            Directory.GetFiles(root, "*.download", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_ChecksumMismatch_RemovesPartialFile()
    {
        var bytes = "tampered installer"u8.ToArray();
        var root = CreateTemporaryDirectory();
        using var http = CreateHttpClient(bytes);
        var service = new UpdateDownloadService(http, root);
        var update = CreateUpdate(bytes) with { Sha256 = new string('0', 64) };

        try
        {
            var action = () => service.DownloadAsync(update);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*SHA-256*");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_SizeMismatch_RemovesPartialFile()
    {
        var bytes = "short installer"u8.ToArray();
        var root = CreateTemporaryDirectory();
        using var http = CreateHttpClient(bytes, reportedLength: bytes.Length + 1);
        var service = new UpdateDownloadService(http, root);

        try
        {
            var action = () => service.DownloadAsync(CreateUpdate(bytes));

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*size mismatch*");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_GitHubReleaseRedirect_DownloadsTrustedAsset()
    {
        var bytes = "github installer"u8.ToArray();
        var root = CreateTemporaryDirectory();
        var redirectUri = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/file.exe?token=test");
        using var http = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = redirectUri },
                };
            }

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = bytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var service = new UpdateDownloadService(http, root);
        var update = CreateUpdate(bytes) with
        {
            DownloadUrl = "https://github.com/masgeek/sms-notification-service/releases/download/v1.1.0/FeeSyncer-Setup-1.1.0.exe",
        };

        try
        {
            using var download = await service.DownloadAsync(update);

            File.Exists(download.Path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_UntrustedRedirect_RejectsDownload()
    {
        var bytes = "redirected installer"u8.ToArray();
        var root = CreateTemporaryDirectory();
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://example.com/FeeSyncer-Setup-1.1.0.exe") },
        }));
        var service = new UpdateDownloadService(http, root);
        var update = CreateUpdate(bytes) with
        {
            DownloadUrl = "https://github.com/masgeek/sms-notification-service/releases/download/1.1.0/FeeSyncer-Setup-1.1.0.exe",
        };

        try
        {
            var action = () => service.DownloadAsync(update);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*untrusted destination*");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static UpdateCheckResult CreateUpdate(byte[] bytes) => new(
        "1.0.0",
        "1.1.0",
        UpdateInstallerFlavor.SelfContained,
        "https://s3.munywele.co.ke/fee-syncer/1.1.0/FeeSyncer-Setup-1.1.0.exe",
        Convert.ToHexString(SHA256.HashData(bytes)),
        bytes.Length,
        DateTimeOffset.UtcNow,
        null,
        false);

    private static HttpClient CreateHttpClient(byte[] bytes, long? reportedLength = null)
    {
        var handler = new StubHandler(_ =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = reportedLength ?? bytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        return new HttpClient(handler);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FeeSyncer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
