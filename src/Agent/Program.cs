using FeeSyncer.Agent;
using FeeSyncer.Shared;
using FeeSyncer.Shared.Logging;
using Microsoft.Extensions.Hosting.WindowsServices;

if (!ConfigPathResolver.IsDevelopment())
    ConfigPathResolver.EnsureMachineConfigFiles();
var builder = Host.CreateApplicationBuilder(args);
var configurationReport = AgentConfiguration.Configure(
    builder.Configuration,
    AppContext.BaseDirectory,
    builder.Environment.EnvironmentName,
    ConfigPathResolver.GetMachineConfigFile(),
    ConfigPathResolver.GetMachineAgentConfigFile(),
    args);
var logRetentionDays = Math.Clamp(builder.Configuration.GetValue("Agent:LogRetentionDays", 7), 1, 365);
var maxLogFileSizeMb = Math.Clamp(builder.Configuration.GetValue("Agent:MaxLogFileSizeMb", 10L), 1, 1024);
SerilogLogging.Configure(
    builder.Logging,
    "Agent",
    builder.Environment.EnvironmentName,
    ConfigPathResolver.GetLogDir(),
    logRetentionDays,
    maxLogFileSizeMb,
    SerilogLogging.GetMinimumLevel(builder.Configuration["Logging:LogLevel:Default"]));

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FeeSyncer.Agent";
});
builder.Services.AddSchoolIntegrationServices(builder.Configuration);

using var host = builder.Build();
var configurationLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FeeSyncer.Agent.Configuration");
var runtimeMode = WindowsServiceHelpers.IsWindowsService()
    ? "WindowsService"
    : Environment.UserInteractive ? "InteractiveConsole" : "ServiceWrapper";
var agentEnabled = builder.Configuration.GetValue("Agent:Enabled", true);
configurationLogger.LogInformation(
    "FeeSyncer Agent host starting. RuntimeMode={RuntimeMode} Enabled={Enabled} LogDirectory={LogDirectory}",
    runtimeMode,
    agentEnabled,
    ConfigPathResolver.GetLogDir());
if (!agentEnabled)
{
    configurationLogger.LogWarning("Agent processing is disabled by Agent:Enabled=false; no processing workers were registered.");
}
AgentConfiguration.LogDebug(
    configurationLogger,
    configurationReport,
    builder.Configuration,
    runtimeMode);
await host.RunAsync();
