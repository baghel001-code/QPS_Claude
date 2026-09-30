using Application.Interfaces.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Admin.Auth;

public static class AuthStoreSetup
{
    public const string ConnectionStringName = "QpsAuth";

    /// <summary>
    /// Registers the SQL Server user store. The in-memory store (seeded test users) is used only
    /// when running in Development AND <c>Auth:UseInMemoryStore</c> is true, so it can never be
    /// switched on in production by a config mistake.
    /// </summary>
    public static IServiceCollection AddQpsAuthUserStore(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(TimeProvider.System);

        if (environment.IsDevelopment() && configuration.GetValue<bool>("Auth:UseInMemoryStore"))
            services.Replace(ServiceDescriptor.Singleton<IAuthUserStore, InMemoryAuthUserStore>()); // must be singleton or data is lost per request
        else
            services.Replace(ServiceDescriptor.Singleton<IAuthUserStore, SqlAuthUserStore>()); // stateless: opens a pooled connection per call

        return services;
    }
}
