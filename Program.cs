using System.Reflection;
using SmsNotificationService;
using SmsNotificationService.Checks;
using SmsNotificationService.Configuration;
using SmsNotificationService.Data;
using SmsNotificationService.Logging;

if (args.Contains("--version") || args.Contains("-v"))
{
    var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
    Console.WriteLine(version);
    return;
}

var builder = Host.CreateApplicationBuilder(args);

var environment = builder.Environment.EnvironmentName;

var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Munywele", "SmsNotificationService");

var logDir = Path.Combine(appDataDir, "logs");

builder.Configuration.AddProductionConfig(appDataDir);

var svcOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName)
    .Get<SmsServiceOptions>() ?? new();

builder.Logging.AddProvider(new FileLoggerProvider(logDir, svcOptions.LogRetentionDays, svcOptions.MaxLogFileSizeMb));
var logger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger<Program>();
logger.LogInformation("[App] SmsNotificationService starting (Environment: {Environment})", environment);

var prodConfigPath = Path.Combine(appDataDir, "appsettings.Production.json");
var appDirConfigPath = Path.Combine(ConfigPathResolver.GetAppDir(), "appsettings.Production.json");

var resolvedConfigPath = ConfigPathResolver.FindConfigFile();
if (File.Exists(resolvedConfigPath))
    logger.LogInformation("[Config] Loading config from: {Path}", resolvedConfigPath);
else
    logger.LogInformation("[Config] No config file found — checked {ProgramData} and {AppDir} — using environment variables or defaults",
        prodConfigPath, appDirConfigPath);

DapperMapper.Register();

builder.Services.AddSmsNotificationServices(builder.Configuration);

builder.Configuration.ValidateSmsServiceOptions();

var host = builder.Build();

var hostLogger = host.Services.GetRequiredService<ILogger<Program>>();

var appOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName)
    .Get<SmsServiceOptions>()!;

hostLogger.LogInformation("[Config] Configuration validated — API: {ApiUrl}", appOptions.SmsApiUrl);

await DatabaseConnectionCheck.RunAsync(appOptions.ConnectionString, hostLogger);

hostLogger.LogInformation("[App] SmsNotificationService ready");
host.Run();
