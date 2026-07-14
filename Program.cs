using SmsNotificationService;

var builder = Host.CreateApplicationBuilder(args);

// Bind typed configuration options
builder.Services.Configure<SmsServiceOptions>(builder.Configuration.GetSection(SmsServiceOptions.SectionName));

// Registers this as a standard Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SmsNotificationService";
});

// Register HttpClient to reuse sockets for external API calls
builder.Services.AddHttpClient();

// Register our background worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Validate configuration before starting services
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var appOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName).Get<SmsServiceOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: SmsService");

if (string.IsNullOrWhiteSpace(appOptions.ConnectionString))
    throw new InvalidOperationException("SmsService:ConnectionString is not configured. Set it via appsettings.json or environment variable SmsService__ConnectionString.");

if (string.IsNullOrWhiteSpace(appOptions.SmsApiUrl))
    throw new InvalidOperationException("SmsService:SmsApiUrl is not configured. Set it via appsettings.json or environment variable SmsService__SmsApiUrl.");

if (!Uri.TryCreate(appOptions.SmsApiUrl, UriKind.Absolute, out _))
    throw new InvalidOperationException($"SmsService:SmsApiUrl is not a valid URI: {appOptions.SmsApiUrl}");

await DatabaseConnectionCheck.RunAsync(appOptions.ConnectionString, logger);

host.Run();
