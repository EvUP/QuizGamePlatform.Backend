using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.HasKey(u => u.Id);
            builder.Property(u => u.KeycloakSubject).HasMaxLength(255).IsRequired();
            builder.HasIndex(u => u.KeycloakSubject).IsUnique();
            builder.Property(u => u.UserName).HasMaxLength(255).IsRequired();

            builder.HasMany(u => u.RoomParticipations)
                .WithOne(rp => rp.User)
                .HasForeignKey(rp => rp.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
