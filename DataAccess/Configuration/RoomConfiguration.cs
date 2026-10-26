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
                .IsRequired();

            // по коду ищут при каждом входе, один код не может вести в две комнаты
            builder.HasIndex(r => r.RoomCode)
                .IsUnique();

            builder.Property(r => r.Status)
                .HasConversion<string>();

            builder.Property(r => r.CreatedAt)
                .HasDefaultValueSql("NOW()");

            // вход, старт и удаление одной комнаты не проходят одновременно, второе сохранение получит конфликт
            builder.Property(r => r.Version)
                .IsRowVersion();

            builder.HasOne(r => r.HostUser)
                .WithMany()
                .HasForeignKey(r => r.HostUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // у игры максимум одно продолжение
            builder.HasOne<RoomEntity>()
                .WithMany()
                .HasForeignKey(r => r.PreviousRoomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(r => r.PreviousRoomId)
                .IsUnique()
                .HasFilter("\"PreviousRoomId\" IS NOT NULL");

            // номер игры в серии один, два параллельных «Сыграть ещё» не создадут две
            builder.HasIndex(r => new { r.SeriesId, r.GameNumber })
                .IsUnique()
                .HasFilter("\"SeriesId\" IS NOT NULL");

            builder.HasMany(r => r.Players)
                .WithOne(rp => rp.Room)
                .HasForeignKey(rp => rp.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}