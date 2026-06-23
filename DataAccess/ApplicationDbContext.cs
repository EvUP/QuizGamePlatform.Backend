using Microsoft.EntityFrameworkCore;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess
{
    public class ApplicationDbContext(IConfiguration config) : DbContext
    {
        private readonly IConfiguration _config = config;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_config.GetConnectionString("DefaultConnection"));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //Применит все конфигурации которые найдет
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
        public DbSet<UserEntity> Users { get; set; } = null!;
    }
}
