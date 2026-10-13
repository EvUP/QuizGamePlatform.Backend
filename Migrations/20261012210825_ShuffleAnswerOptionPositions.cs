using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ShuffleAnswerOptionPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // в сиде правильный вариант всегда был первым, перемешиваем позиции
            // через отрицательные, чтобы не упереться в уникальный (QuestionId, Position)
            migrationBuilder.Sql("""
                UPDATE "AnswerOptions" a
                SET "Position" = -s.new_position
                FROM (
                    SELECT "Id", row_number() OVER (PARTITION BY "QuestionId" ORDER BY random()) AS new_position
                    FROM "AnswerOptions"
                ) s
                WHERE a."Id" = s."Id";

                UPDATE "AnswerOptions" SET "Position" = -"Position";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // откатывать нечего, порядок вариантов просто случайный
        }
    }
}
