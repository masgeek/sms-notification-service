using FeeSyncer.Sms;
using FeeSyncer.Sms.Checks;
using FeeSyncer.Sms.Configuration;
using FeeSyncer.Sms.Data;
using FeeSyncer.Shared;
using FeeSyncer.Shared.Logging;
using Serilog;

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine(VersionHelper.GetCurrentVersion());
    return;
}

var builder = Host.CreateApplicationBuilder(args);

var environment = builder.Environment.EnvironmentName;

builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

if (!ConfigPathResolver.IsDevelopment())
    builder.Configuration.AddJsonFile(ConfigPathResolver.GetMachineConfigFile(), optional: true, reloadOnChange: false);

var baseUrl = builder.Configuration["FeeSyncer:BaseUrl"];
if (!string.IsNullOrWhiteSpace(baseUrl))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SmsService:SmsApiUrl"] = ConfigReader.CombineUrl(
            baseUrl,
            builder.Configuration["FeeSyncer:ApiEndpoints:SmsNotifications"] ?? Constants.DefaultSmsNotificationsEndpoint)
    });
}

var logDir = ConfigPathResolver.GetLogDir();

var svcOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName)
    .Get<SmsServiceOptions>() ?? new();

SerilogLogging.Configure(
    builder.Logging,
    "Sms",
    environment,
    logDir,
    svcOptions.LogRetentionDays,
    svcOptions.MaxLogFileSizeMb,
    SerilogLogging.GetMinimumLevel(builder.Configuration["Logging:LogLevel:Default"]));
var logger = Log.ForContext<Program>();
logger.Information("[App] FeeSyncer.Sms starting (Environment: {Environment})", environment);

var resolvedConfigPath = ConfigPathResolver.FindConfigFile();
if (File.Exists(resolvedConfigPath))
    logger.Information("[Config] Loading config from: {Path}", resolvedConfigPath);
else
    logger.Information("[Config] No config file found - using environment variables or defaults");

DapperMapper.Register();

builder.Services.AddSmsServices(builder.Configuration);

builder.Configuration.ValidateSmsServiceOptions();

using var host = builder.Build();

var hostLogger = host.Services.GetRequiredService<ILogger<Program>>();

var appOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName)
    .Get<SmsServiceOptions>()!;

hostLogger.LogInformation("[Config] Configuration validated — API: {ApiUrl}", appOptions.SmsApiUrl);

await DatabaseConnectionCheck.RunAsync(appOptions.ConnectionString, hostLogger);

hostLogger.LogInformation("[App] FeeSyncer.Sms ready");
host.Run();
