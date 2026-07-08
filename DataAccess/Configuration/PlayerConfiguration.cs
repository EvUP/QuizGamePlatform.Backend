using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class PlayerConfiguration : IEntityTypeConfiguration<PlayerEntity>
    {
        public void Configure(EntityTypeBuilder<PlayerEntity> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.UserName).HasMaxLength(50).IsRequired();

            builder.HasMany(p => p.RoomParticipations)
                .WithOne(rp => rp.Player)
                .HasForeignKey(rp => rp.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
