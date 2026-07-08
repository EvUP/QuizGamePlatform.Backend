using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class RoomConfiguration : IEntityTypeConfiguration<RoomEntity>
    {
        public void Configure(EntityTypeBuilder<RoomEntity> builder)
        {
            builder.HasKey(r => r.Id);

            builder.Property(r => r.RoomCode)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(r => r.Status)
                .HasConversion<string>();

            builder.Property(r => r.CreatedAt)
                .HasDefaultValueSql("NOW()");

            builder.HasMany(r => r.Players)
                .WithOne(rp => rp.Room)
                .HasForeignKey(rp => rp.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}