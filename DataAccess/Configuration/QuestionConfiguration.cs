using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Configuration
{
    public class QuestionConfiguration : IEntityTypeConfiguration<QuestionEntity>
    {
        public void Configure(EntityTypeBuilder<QuestionEntity> builder)
        {
            builder.HasKey(q => q.Id);

            builder.Property(q => q.Text)
                .IsRequired()
                .HasMaxLength(2000);

            builder.Property(q => q.Order)
                .IsRequired();

            builder.HasIndex(q => new { q.RoomId, q.Order });

            builder.HasOne(q => q.Room)
                .WithMany(r => r.Questions)
                .HasForeignKey(q => q.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}