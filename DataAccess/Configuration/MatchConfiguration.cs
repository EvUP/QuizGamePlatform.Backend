using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class MatchConfiguration : IEntityTypeConfiguration<MatchEntity>
    {
        public void Configure(EntityTypeBuilder<MatchEntity> builder)
        {
            builder.HasKey(m => m.Id);

            builder.Property(m => m.Status)
                .HasConversion<string>();

            builder.Property(m => m.StartedAt)
                .HasDefaultValueSql("NOW()");

            builder.Property(m => m.QuestionEndsAt)
                .HasDefaultValueSql("NOW()");

            // под опрос таймера
            builder.HasIndex(m => new { m.Status, m.QuestionEndsAt });

            // одна незавершённая партия на комнату
            builder.HasIndex(m => m.RoomId)
                .IsUnique()
                .HasFilter("\"Status\" <> 'Finished'");

            builder.HasOne(m => m.Room)
                .WithMany()
                .HasForeignKey(m => m.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(m => m.Questions)
                .WithOne(mq => mq.Match)
                .HasForeignKey(mq => mq.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
