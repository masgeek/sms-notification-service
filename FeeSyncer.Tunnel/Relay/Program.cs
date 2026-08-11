using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using FeeSyncer.Tunnel.Protocol;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TunnelRegistry>();

var app = builder.Build();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Map("/connect", async (HttpContext context, TunnelRegistry registry, CancellationToken cancellationToken) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var register = await ReceiveJsonAsync<RegisterMessage>(socket, cancellationToken);
    if (register is null || register.Type != TunnelMessageType.Register)
    {
        await SendJsonAsync(socket, new CloseMessage(TunnelMessageType.Close, "INVALID_REGISTER", "A register message is required."), cancellationToken);
        return;
    }

    if (!registry.TryRegister(register, socket, out var registrationError))
    {
        await SendJsonAsync(socket, new CloseMessage(TunnelMessageType.Close, "REGISTER_REJECTED", registrationError!), cancellationToken);
        return;
    }

    await SendJsonAsync(socket, new RegisterAckMessage(
        TunnelMessageType.RegisterAck,
        1,
        $"{register.SchoolSlug}.munywele.co.ke",
        30), cancellationToken);

    try
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var heartbeat = await ReceiveJsonAsync<HeartbeatMessage>(socket, cancellationToken);
            if (heartbeat?.Type == TunnelMessageType.Heartbeat)
            {
                registry.Touch(register.TunnelId);
            }
        }
    }
    finally
    {
        registry.Remove(register.TunnelId, socket);
    }
});

app.Run();

static async Task<T?> ReceiveJsonAsync<T>(WebSocket socket, CancellationToken cancellationToken)
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

static Task SendJsonAsync<T>(WebSocket socket, T message, CancellationToken cancellationToken)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(message, TunnelJson.Options);
    return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
}

public sealed class TunnelRegistry
{
    private readonly ConcurrentDictionary<string, Registration> registrations = new(StringComparer.Ordinal);

    public bool TryRegister(RegisterMessage message, WebSocket socket, out string? error)
    {
        error = null;
        if (message.Version != 1 || string.IsNullOrWhiteSpace(message.TunnelId) || string.IsNullOrWhiteSpace(message.SchoolSlug))
        {
            error = "The tunnel registration is incomplete or unsupported.";
            return false;
        }

        if (!registrations.TryAdd(message.TunnelId, new Registration(message.SchoolSlug, socket)))
        {
            error = "The tunnel ID is already connected.";
            return false;
        }

        return true;
    }

    public void Touch(string tunnelId)
    {
        if (registrations.TryGetValue(tunnelId, out var registration))
        {
            registration.LastSeenAt = DateTimeOffset.UtcNow;
        }
    }

    public void Remove(string tunnelId, WebSocket socket)
    {
        if (registrations.TryGetValue(tunnelId, out var registration) && ReferenceEquals(registration.Socket, socket))
        {
            registrations.TryRemove(tunnelId, out _);
        }
    }

    private sealed class Registration(string schoolSlug, WebSocket socket)
    {
        public string SchoolSlug { get; } = schoolSlug;
        public WebSocket Socket { get; } = socket;
        public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
