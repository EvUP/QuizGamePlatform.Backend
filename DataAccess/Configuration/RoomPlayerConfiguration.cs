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

            // вмещает логин аккаунта любой длины
            builder.Property(rp => rp.Nickname).HasMaxLength(255).IsRequired();

            // ники гостей уникальны в комнате, аккаунт отличается логином
            builder.HasIndex(rp => new { rp.RoomId, rp.Nickname })
                .IsUnique()
                .HasFilter("\"UserId\" IS NULL");

            // одно участие на аккаунт, null гостей не мешают
            builder.HasIndex(rp => new { rp.RoomId, rp.UserId })
                .IsUnique();

            builder.Property(rp => rp.ExitReason)
                .HasConversion<string>();

            builder.HasOne(rp => rp.Room)
                .WithMany(r => r.Players)
                .HasForeignKey(rp => rp.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
