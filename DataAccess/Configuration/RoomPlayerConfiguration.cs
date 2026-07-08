using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class RoomPlayerConfiguration : IEntityTypeConfiguration<RoomPlayerEntity>
    {
        public void Configure(EntityTypeBuilder<RoomPlayerEntity> builder)
        {
            builder.HasKey(rp => rp.Id);
            builder.Property(rp => rp.JoinedAt)
                .HasDefaultValueSql("NOW()");

            // Одна комната - один уникальный участник
            builder.HasIndex(rp => new { rp.RoomId, rp.PlayerId })
                .IsUnique();

            builder.Property(rp => rp.ExitReason)
                .HasConversion<string>();

            builder.HasOne(rp => rp.Room)
                .WithMany(r => r.Players)
                .HasForeignKey(rp => rp.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(rp => rp.Player)
                .WithMany(p => p.RoomParticipations)
                .HasForeignKey(rp => rp.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}