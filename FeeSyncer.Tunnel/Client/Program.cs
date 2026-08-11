using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<TunnelClientOptions>(builder.Configuration.GetSection("Tunnel"));
builder.Services.AddHostedService<TunnelClientWorker>();
builder.Services.AddWindowsService(options => options.ServiceName = "FeeProcessor Tunnel");

await builder.Build().RunAsync();

public sealed class TunnelClientOptions
{
    public bool Enabled { get; set; }
    public string RelayUrl { get; set; } = "wss://tunnel.munywele.co.ke/connect";
    public string SchoolSlug { get; set; } = "";
    public string TunnelId { get; set; } = "";
    public string Credential { get; set; } = "";
    public string OriginUrl { get; set; } = "http://127.0.0.1:8001";
    public int HeartbeatSeconds { get; set; } = 30;
    public int ReconnectMinSeconds { get; set; } = 1;
    public int ReconnectMaxSeconds { get; set; } = 60;
}

public sealed class TunnelClientWorker(
    IOptions<TunnelClientOptions> options,
    ILogger<TunnelClientWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("School tunnel is disabled.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        logger.LogInformation("School tunnel is ready for {SchoolSlug} and origin {OriginUrl}.",
            options.Value.SchoolSlug, options.Value.OriginUrl);

        // Registration and request streaming are implemented in the next slice.
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
