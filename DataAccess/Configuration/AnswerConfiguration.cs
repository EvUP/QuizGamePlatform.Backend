using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Configuration
{
    public class AnswerOptionConfiguration : IEntityTypeConfiguration<AnswerOptionEntity>
    {
        public void Configure(EntityTypeBuilder<AnswerOptionEntity> builder)
        {
            builder.HasKey(a => a.Id);

            builder.Property(a => a.Text)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(a => a.Position)
                .IsRequired();

            builder.HasIndex(a => new { a.QuestionId, a.Position })
                .IsUnique();

            builder.HasOne(a => a.Question)
                .WithMany(q => q.AnswerOptions)
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}