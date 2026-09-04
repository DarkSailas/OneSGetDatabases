using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;
using OneSGetDatabases.Core.Services;

namespace OneSGetDatabases.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOneSGetDatabasesCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Options
        services.Configure<RacConfig>(configuration.GetSection("Rac"));
        services.Configure<ConsulConfig>(configuration.GetSection("Consul"));
        services.Configure<ActiveDirectoryConfig>(configuration.GetSection("ActiveDirectory"));
        services.Configure<ConfluenceConfig>(configuration.GetSection("Confluence"));
        services.Configure<SchedulerConfig>(configuration.GetSection("Scheduler"));
        services.Configure<EmailConfig>(configuration.GetSection("Email"));
        services.Configure<DbmsConnectionConfig>(configuration.GetSection("Dbms"));
        services.Configure<UiConfig>(configuration.GetSection("Ui"));
        services.Configure<List<ClusterConfig>>(configuration.GetSection("Clusters"));
        services.Configure<ClusterDiscoveryConfig>(configuration.GetSection("ClusterDiscovery"));
        services.Configure<AuditLogConfig>(configuration.GetSection("AuditLog"));

        // Singletons / Scoped Services
        services.AddSingleton<IDatabaseCacheService, DatabaseCacheService>();
        services.AddSingleton<IRacService, RacService>();
        services.AddSingleton<ICimService, CimService>();
        services.AddSingleton<IClusterDiscoveryEngine, ClusterDiscoveryEngine>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddSingleton<IOneSServiceManager, OneSServiceManager>();
        services.AddSingleton<IActiveDirectoryService, ActiveDirectoryService>();
        services.AddSingleton<IDbmsInspectorService, DbmsInspectorService>();
        services.AddSingleton<INotificationService, EmailNotificationService>();

        // Http Clients
        services.AddHttpClient<IConsulService, ConsulService>();
        services.AddHttpClient<IConfluencePublisher, ConfluencePublisher>();

        services.AddScoped<IInfobaseDiscoveryService, InfobaseDiscoveryService>();

        return services;
    }
}
