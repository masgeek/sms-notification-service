using SmsNotificationService;
using SmsNotificationService.Checks;
using SmsNotificationService.Configuration;
using SmsNotificationService.Data;
using SmsNotificationService.Logging;
using SmsNotificationService.Shared;

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine(VersionHelper.GetCurrentVersion());
    return;
}

var builder = Host.CreateApplicationBuilder(args);

var environment = builder.Environment.EnvironmentName;

var appDataDir = ConfigPathResolver.GetProgramDataDir();
var logDir = ConfigPathResolver.GetLogDir();

builder.Configuration.AddProductionConfig(appDataDir);

var svcOptions = builder.Configuration.GetSection(SmsServiceOptions.SectionName)
    .Get<SmsServiceOptions>() ?? new();

builder.Logging.AddProvider(new FileLoggerProvider(logDir, svcOptions.LogRetentionDays, svcOptions.MaxLogFileSizeMb));
var logger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger<Program>();
logger.LogInformation("[App] SmsNotificationService starting (Environment: {Environment})", environment);

var resolvedConfigPath = ConfigPathResolver.FindConfigFile();
if (File.Exists(resolvedConfigPath))
    logger.LogInformation("[Config] Loading config from: {Path}", resolvedConfigPath);
else
    logger.LogInformation("[Config] No config file found — checked ProgramData and AppDir — using environment variables or defaults");

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
