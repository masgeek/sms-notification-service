using FeeSyncer.Agent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FeeSyncer.Agent";
});
builder.Services.AddSchoolIntegrationServices(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
