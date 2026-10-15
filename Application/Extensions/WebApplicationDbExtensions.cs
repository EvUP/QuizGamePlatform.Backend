using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using QuizGamePlatform.Backend.DataAccess.Caching;
using QuizGamePlatform.Backend.DataAccess.Seed;
using QuizGamePlatform.Backend.DataAccess;

namespace QuizGamePlatform.Backend.Application.Extensions
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
            var changed = await DbSeeder.SeedAsync(db, env.ContentRootPath, cancellationToken);

            // кэш тем скользящий и сам не истечёт, пока к нему ходят — сбрасываем после досева
            if (changed.Count > 0)
            {
                var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
                await cache.RemoveAsync(RedisKeysFactory.CategoriesListKey, cancellationToken);

                foreach (var categoryId in changed)
                    await cache.RemoveAsync(RedisKeysFactory.QuestionsByCategory(categoryId), cancellationToken);
            }
        }

    }
}
