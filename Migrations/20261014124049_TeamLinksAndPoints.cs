using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class TeamLinksAndPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpectedPlayers",
                table: "RoomTeams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JoinCode",
                table: "RoomTeams",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "MatchQuestions",
                type: "timestamp with time zone",
                nullable: true);

            // уже созданным командам свой код, иначе уникальный индекс не построится
            migrationBuilder.Sql(
                "UPDATE \"RoomTeams\" SET \"JoinCode\" = upper(substr(md5(\"RoomId\"::text || \"Number\"::text), 1, 12)) WHERE \"JoinCode\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTeams_JoinCode",
                table: "RoomTeams",
                column: "JoinCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomTeams_JoinCode",
                table: "RoomTeams");

            migrationBuilder.DropColumn(
                name: "ExpectedPlayers",
                table: "RoomTeams");

            migrationBuilder.DropColumn(
                name: "JoinCode",
                table: "RoomTeams");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "MatchQuestions");
        }
    }
}
