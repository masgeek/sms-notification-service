using FeeSyncer.Agent;
using FeeSyncer.Shared;

if (!ConfigPathResolver.IsDevelopment())
    ConfigPathResolver.EnsureMachineConfigFiles();
var builder = Host.CreateApplicationBuilder(args);
var environmentName = builder.Environment.EnvironmentName;
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

if (ConfigPathResolver.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
}
else
{
    builder.Configuration.AddJsonFile(ConfigPathResolver.GetMachineAgentConfigFile(), optional: true, reloadOnChange: false);
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FeeSyncer.Agent";
});
builder.Services.AddSchoolIntegrationServices(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
