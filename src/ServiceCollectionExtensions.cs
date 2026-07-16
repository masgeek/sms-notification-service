using Microsoft.Extensions.Configuration;
using SmsNotificationService.Configuration;
using SmsNotificationService.Data;
using SmsNotificationService.Services;
using SmsNotificationService.Workers;
using Microsoft.Extensions.Options;

namespace SmsNotificationService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmsNotificationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmsServiceOptions>(
            configuration.GetSection(SmsServiceOptions.SectionName));

        services.AddWindowsService(options =>
        {
            options.ServiceName = "SmsNotificationService";
        });

        services.AddHttpClient();

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
