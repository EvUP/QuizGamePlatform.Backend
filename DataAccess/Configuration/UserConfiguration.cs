using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Configuration
{
    public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.HasKey(k => k.Id);
            builder.Property(k => k.Name).HasMaxLength(100);
            builder.Property(k => k.Surname).HasMaxLength(100);
        }
    }
}