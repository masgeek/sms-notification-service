using FeeSyncer.Agent;
using FeeSyncer.Shared;
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

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FeeSyncer.Agent";
});
builder.Services.AddSchoolIntegrationServices(builder.Configuration);

var host = builder.Build();
var configurationLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FeeSyncer.Agent.Configuration");
AgentConfiguration.LogDebug(
    configurationLogger,
    configurationReport,
    builder.Configuration,
    WindowsServiceHelpers.IsWindowsService() ? "WindowsService" : "InteractiveConsole");
await host.RunAsync();
