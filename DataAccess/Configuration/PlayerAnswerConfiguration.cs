using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class PlayerAnswerConfiguration : IEntityTypeConfiguration<PlayerAnswerEntity>
    {
        public void Configure(EntityTypeBuilder<PlayerAnswerEntity> builder)
        {
            builder.HasKey(pa => pa.Id);

            // Один игрок — один ответ на вопрос матча
            builder.HasIndex(pa => new { pa.MatchQuestionId, pa.PlayerId })
                .IsUnique();

            builder.Property(pa => pa.AnsweredAt)
                .HasDefaultValueSql("NOW()");

            builder.HasOne(pa => pa.Player)
                .WithMany()
                .HasForeignKey(pa => pa.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(pa => pa.SelectedOption)
                .WithMany()
                .HasForeignKey(pa => pa.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
