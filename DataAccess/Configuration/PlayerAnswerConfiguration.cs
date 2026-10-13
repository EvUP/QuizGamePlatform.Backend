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

            // один ответ участника на вопрос
            builder.HasIndex(pa => new { pa.MatchQuestionId, pa.RoomPlayerId })
                .IsUnique();

            builder.Property(pa => pa.AnsweredAt)
                .HasDefaultValueSql("NOW()");

            // удаляются вместе с комнатой
            builder.HasOne(pa => pa.RoomPlayer)
                .WithMany()
                .HasForeignKey(pa => pa.RoomPlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(pa => pa.SelectedOption)
                .WithMany()
                .HasForeignKey(pa => pa.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
