using Microsoft.EntityFrameworkCore;
using testBdControllers.DataAccess.Configuration;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //Применит все конфигурации которые найдет
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserConfiguration).Assembly);
        }
        public DbSet<UserEntity> Users { get; set; } = null!;
    }
}
