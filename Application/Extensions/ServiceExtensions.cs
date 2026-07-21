using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.DataAccess;

namespace QuizGamePlatform.Backend.Application.Extensions
{
    public static class ServiceExtensions
    {
        private static string? GetConnection(string connectionName, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(connectionName)
               ?? throw new InvalidOperationException($"Connection string {connectionName} not found.");

            return connectionString;
        }

        public static IServiceCollection AddApplicationDbContext(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString = GetConnection("DefaultConnection", configuration);

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
            });

            return services;
        }

        public static IServiceCollection AddRedisCache(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString = GetConnection("Redis", configuration);

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = "quiz-platform-redis";
            });

            return services;
        }
    }
}