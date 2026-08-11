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

    if (!registry.TryRegister(register, socket, out var error))
    {
        await SendJsonAsync(socket, new CloseMessage(TunnelMessageType.Close, "REGISTER_REJECTED", error!), cancellationToken);
        return;
    }

    await SendJsonAsync(socket, new RegisterAckMessage(TunnelMessageType.RegisterAck, 1,
        $"{register.SchoolSlug}.munywele.co.ke", 30), cancellationToken);

    try
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var document = await ReceiveJsonAsync<JsonDocument>(socket, cancellationToken);
            if (document is null)
            {
                break;
            }

            var type = document.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "heartbeat")
            {
                registry.Touch(register.TunnelId);
            }
            else if (type == "response_end")
            {
                var response = document.RootElement.Deserialize<ResponseEndMessage>(TunnelJson.Options);
                if (response is not null)
                {
                    registry.Complete(register.TunnelId, response);
                }
            }
        }
    }
    finally
    {
        registry.Remove(register.TunnelId, socket);
    }
});

app.Map("/{**path}", async (HttpContext context, TunnelRegistry registry, CancellationToken cancellationToken) =>
{
    var schoolSlug = context.Request.Host.Host.Split('.', 2)[0];
    if (!registry.TryGetBySchoolSlug(schoolSlug, out var tunnel))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    await using var body = new MemoryStream();
    await context.Request.Body.CopyToAsync(body, cancellationToken);
    if (body.Length > 4 * 1024 * 1024)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var request = new RequestStartMessage(
        TunnelMessageType.RequestStart,
        Guid.NewGuid().ToString("N"),
        context.Request.Method,
        context.Request.Path.Value ?? "/",
        context.Request.QueryString.HasValue ? context.Request.QueryString.Value![1..] : null,
        context.Request.Headers.ToDictionary(pair => pair.Key,
            pair => pair.Value.Select(value => value ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase),
        body.Length == 0 ? null : Convert.ToBase64String(body.ToArray()));
    var response = await tunnel.ProxyAsync(request, cancellationToken);

    context.Response.StatusCode = response.Status;
    foreach (var header in response.Headers)
    {
        if (!string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers[header.Key] = header.Value;
        }
    }

    if (!string.IsNullOrEmpty(response.BodyBase64))
    {
        await context.Response.Body.WriteAsync(Convert.FromBase64String(response.BodyBase64), cancellationToken);
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
    private readonly ConcurrentDictionary<string, TunnelConnection> registrations = new(StringComparer.Ordinal);

    public bool TryRegister(RegisterMessage message, WebSocket socket, out string? error)
    {
        error = null;
        if (message.Version != 1 || string.IsNullOrWhiteSpace(message.TunnelId) || string.IsNullOrWhiteSpace(message.SchoolSlug))
        {
            error = "The tunnel registration is incomplete or unsupported.";
            return false;
        }

        if (!registrations.TryAdd(message.TunnelId, new TunnelConnection(message.TunnelId, message.SchoolSlug, socket)))
        {
            error = "The tunnel ID is already connected.";
            return false;
        }

        return true;
    }

    public bool TryGetBySchoolSlug(string schoolSlug, out TunnelConnection tunnel)
    {
        tunnel = registrations.Values.FirstOrDefault(item => string.Equals(item.SchoolSlug, schoolSlug, StringComparison.OrdinalIgnoreCase))!;
        return tunnel is not null;
    }

    public void Touch(string tunnelId)
    {
        if (registrations.TryGetValue(tunnelId, out var registration))
        {
            registration.LastSeenAt = DateTimeOffset.UtcNow;
        }
    }

    public void Complete(string tunnelId, ResponseEndMessage response)
    {
        if (registrations.TryGetValue(tunnelId, out var registration))
        {
            registration.Complete(response);
        }
    }

    public void Remove(string tunnelId, WebSocket socket)
    {
        if (registrations.TryGetValue(tunnelId, out var registration) && ReferenceEquals(registration.Socket, socket))
        {
            registrations.TryRemove(tunnelId, out _);
        }
    }

    public sealed class TunnelConnection(string tunnelId, string schoolSlug, WebSocket socket)
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ResponseEndMessage>> pending = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim sendLock = new(1, 1);
        public string TunnelId { get; } = tunnelId;
        public string SchoolSlug { get; } = schoolSlug;
        public WebSocket Socket { get; } = socket;
        public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

        public async Task<ResponseEndMessage> ProxyAsync(RequestStartMessage request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<ResponseEndMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[request.RequestId] = completion;
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(request, TunnelJson.Options);
                await sendLock.WaitAsync(cancellationToken);
                try
                {
                    await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
                }
                finally
                {
                    sendLock.Release();
                }

                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(120), cancellationToken);
            }
            finally
            {
                pending.TryRemove(request.RequestId, out _);
            }
        }

        public void Complete(ResponseEndMessage response)
        {
            if (pending.TryGetValue(response.RequestId, out var completion))
            {
                completion.TrySetResult(response);
            }
        }
    }
}
