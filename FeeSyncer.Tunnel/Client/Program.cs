using System.Net.WebSockets;
using System.Text.Json;
using FeeSyncer.Tunnel.Protocol;
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
}

public sealed class TunnelClientWorker(IOptions<TunnelClientOptions> options, ILogger<TunnelClientWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("School tunnel is disabled.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(options.Value.RelayUrl), stoppingToken);
        await SendJsonAsync(socket, new RegisterMessage(TunnelMessageType.Register, 1,
            options.Value.TunnelId, options.Value.SchoolSlug, options.Value.Credential, "1.0.0"), stoppingToken);

        var acknowledgement = await ReceiveJsonAsync<RegisterAckMessage>(socket, stoppingToken);
        if (acknowledgement?.Type != TunnelMessageType.RegisterAck)
        {
            throw new InvalidOperationException("Tunnel registration was rejected.");
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri(options.Value.OriginUrl) };
        logger.LogInformation("Tunnel registered for {SchoolSlug}.", options.Value.SchoolSlug);
        while (!stoppingToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var request = await ReceiveJsonAsync<RequestStartMessage>(socket, stoppingToken);
            if (request?.Type != TunnelMessageType.RequestStart)
            {
                continue;
            }

            var response = await ForwardAsync(httpClient, request, stoppingToken);
            await SendJsonAsync(socket, response, stoppingToken);
        }
    }

    private static async Task<ResponseEndMessage> ForwardAsync(HttpClient client, RequestStartMessage request, CancellationToken cancellationToken)
    {
        var uri = request.Path + (string.IsNullOrEmpty(request.Query) ? "" : $"?{request.Query}");
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
        if (!string.IsNullOrEmpty(request.BodyBase64))
        {
            message.Content = new ByteArrayContent(Convert.FromBase64String(request.BodyBase64));
        }

        foreach (var header in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value) && message.Content is not null)
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.SelectMany(value => value.Value).ToArray(), StringComparer.OrdinalIgnoreCase);

        return new ResponseEndMessage(TunnelMessageType.ResponseEnd, request.RequestId, (int)response.StatusCode,
            headers, body.Length == 0 ? null : Convert.ToBase64String(body));
    }

    private static async Task<T?> ReceiveJsonAsync<T>(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        await using var output = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return default;
            }

            await output.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        } while (!result.EndOfMessage);

        return JsonSerializer.Deserialize<T>(output.ToArray(), TunnelJson.Options);
    }

    private static Task SendJsonAsync<T>(ClientWebSocket socket, T message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, TunnelJson.Options);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}
