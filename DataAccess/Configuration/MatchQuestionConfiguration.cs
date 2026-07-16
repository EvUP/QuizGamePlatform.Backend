using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class MatchQuestionConfiguration : IEntityTypeConfiguration<MatchQuestionEntity>
    {
        public void Configure(EntityTypeBuilder<MatchQuestionEntity> builder)
        {
            builder.HasKey(mq => mq.Id);

            // Порядок вопроса уникален в рамках матча
            builder.HasIndex(mq => new { mq.MatchId, mq.Order })
                .IsUnique();

            builder.HasOne(mq => mq.Question)
                .WithMany()
                .HasForeignKey(mq => mq.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(mq => mq.Answers)
                .WithOne(pa => pa.MatchQuestion)
                .HasForeignKey(pa => pa.MatchQuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
