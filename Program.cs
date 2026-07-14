using SmsNotificationService.Checks;
using SmsNotificationService.Configuration;
using SmsNotificationService.Data;
using SmsNotificationService.Models;
using SmsNotificationService.Services;
using SmsNotificationService.Workers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Dapper;

var builder = Host.CreateApplicationBuilder(args);

var environment = builder.Environment.EnvironmentName;
var logger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger<Program>();
logger.LogInformation("[App] SmsNotificationService starting (Environment: {Environment})", environment);

// Map PascalCase model properties to snake_case DB columns for Dapper
SqlMapper.SetTypeMap(typeof(SmsNotification), new CustomPropertyTypeMap(
    typeof(SmsNotification),
    (type, columnName) => type.GetProperty(
        columnName switch
        {
            "id" => nameof(SmsNotification.Id),
            "phone_number" => nameof(SmsNotification.PhoneNumber),
            "mpesa_code" => nameof(SmsNotification.MpesaCode),
            "adm_no" => nameof(SmsNotification.AdmNo),
            "stud_names" => nameof(SmsNotification.StudNames),
            "amount" => nameof(SmsNotification.Amount),
            "receipt_no" => nameof(SmsNotification.ReceiptNo),
            "dated" => nameof(SmsNotification.Dated),
            "status" => nameof(SmsNotification.Status),
            "max_retries" => nameof(SmsNotification.MaxRetries),
            "retry_count" => nameof(SmsNotification.RetryCount),
            "retry_after" => nameof(SmsNotification.RetryAfter),
            "created_at" => nameof(SmsNotification.CreatedAt),
            "updated_at" => nameof(SmsNotification.UpdatedAt),
            _ => columnName
        })!));

// Bind typed configuration options
builder.Services.Configure<SmsServiceOptions>(builder.Configuration.GetSection(SmsServiceOptions.SectionName));

// Registers this as a standard Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SmsNotificationService";
});

// Register HttpClient to reuse sockets for external API calls
builder.Services.AddHttpClient();

// Register services
builder.Services.AddSingleton<INotificationRepository>(sp =>
{
    var options = sp.GetRequiredService<IOptions<SmsServiceOptions>>();
    var logger = sp.GetRequiredService<ILogger<NotificationRepository>>();
    return new NotificationRepository(options.Value.ConnectionString, logger);
});

builder.Services.AddSingleton<SqlDependencyListener>(sp =>
{
    var options = sp.GetRequiredService<IOptions<SmsServiceOptions>>();
    var logger = sp.GetRequiredService<ILogger<SqlDependencyListener>>();
    return new SqlDependencyListener(options.Value.ConnectionString, logger);
});

builder.Services.AddSingleton<ISmsSender, SmsApiService>();

// Register our background worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Validate configuration before starting services
var hostLogger = host.Services.GetRequiredService<ILogger<Program>>();
var appOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName).Get<SmsServiceOptions>()
    ?? throw new InvalidOperationException("[Config] Missing configuration section: SmsService");

if (string.IsNullOrWhiteSpace(appOptions.ConnectionString))
    throw new InvalidOperationException("[Config] SmsService:ConnectionString is not configured. Set via appsettings.json or SmsService__ConnectionString.");

if (string.IsNullOrWhiteSpace(appOptions.SmsApiUrl))
    throw new InvalidOperationException("[Config] SmsService:SmsApiUrl is not configured. Set via appsettings.json or SmsService__SmsApiUrl.");

if (!Uri.TryCreate(appOptions.SmsApiUrl, UriKind.Absolute, out _))
    throw new InvalidOperationException($"[Config] SmsService:SmsApiUrl is not a valid URI: {appOptions.SmsApiUrl}");

if (string.IsNullOrWhiteSpace(appOptions.AuthorizationToken))
    throw new InvalidOperationException("[Config] SmsService:AuthorizationToken is not configured. Set via appsettings.json or SmsService__AuthorizationToken.");

hostLogger.LogInformation("[Config] Configuration validated — API: {ApiUrl}", appOptions.SmsApiUrl);

await DatabaseConnectionCheck.RunAsync(appOptions.ConnectionString, hostLogger);

hostLogger.LogInformation("[App] SmsNotificationService ready");
host.Run();
