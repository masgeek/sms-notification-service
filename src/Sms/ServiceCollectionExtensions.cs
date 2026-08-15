using FeeSyncer.Sms.Configuration;
using FeeSyncer.Sms.Data;
using FeeSyncer.Sms.Services;
using FeeSyncer.Sms.Workers;
using Microsoft.Extensions.Options;

namespace FeeSyncer.Sms;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmsServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmsServiceOptions>(
            configuration.GetSection(SmsServiceOptions.SectionName));

        services.AddWindowsService(options =>
        {
            options.ServiceName = "FeeSyncer.Sms";
        });

        services.AddHttpClient("SmsApi");

        services.AddSingleton<INotificationRepository>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SmsServiceOptions>>();
            var logger = sp.GetRequiredService<ILogger<NotificationRepository>>();
            return new NotificationRepository(options.Value.ConnectionString, logger);
        });

        services.AddSingleton<SqlDependencyListener>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SmsServiceOptions>>();
            var logger = sp.GetRequiredService<ILogger<SqlDependencyListener>>();
            return new SqlDependencyListener(options.Value.ConnectionString, logger);
        });

        services.AddSingleton<ISmsSender, SmsApiService>();
        services.AddSingleton<NotificationProcessor>();
        services.AddHostedService<TableChangeListener>();
        services.AddHostedService<RetryPoller>();
        return services;
    }
}
