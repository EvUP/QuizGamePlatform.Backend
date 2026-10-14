using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.Core.Helpers;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class RoomTeamConfiguration : IEntityTypeConfiguration<RoomTeamEntity>
    {
        public void Configure(EntityTypeBuilder<RoomTeamEntity> builder)
        {
            builder.HasKey(t => new { t.RoomId, t.Number });

            builder.Property(t => t.Name).HasMaxLength(MatchHelper.TeamNameMaxLength).IsRequired();

            builder.HasIndex(t => new { t.RoomId, t.Name }).IsUnique();

            builder.Property(t => t.JoinCode).IsRequired();
            builder.HasIndex(t => t.JoinCode).IsUnique();

            builder.HasOne(t => t.Room)
                .WithMany(r => r.Teams)
                .HasForeignKey(t => t.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
