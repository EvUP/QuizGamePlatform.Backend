using Microsoft.EntityFrameworkCore;
using testBdControllers.DataAccess.Seed;
using testBdControllers.DataAccess;

namespace testBdControllers.Application.Extensions
{
    public static class WebApplicationDbExtensions
    {
        public static async Task MigrateAndSeedIfNeededAsync(
            this WebApplication app,
            CancellationToken cancellationToken = default)
        {
            using var scope = app.Services.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            await db.Database.MigrateAsync(cancellationToken);
            await DbSeeder.SeedAsync(db, env.ContentRootPath, cancellationToken);
        }

    }
}
