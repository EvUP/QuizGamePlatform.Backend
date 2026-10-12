using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAndRoomPlayerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerAnswers_Players_PlayerId",
                table: "PlayerAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_RoomParticipations_Players_PlayerId",
                table: "RoomParticipations");

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "RoomParticipations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "RoomParticipations",
                type: "uuid",
                nullable: true);

            // ник переезжает из Players в участие
            migrationBuilder.Sql("""
                UPDATE "RoomParticipations" rp
                SET "Nickname" = p."UserName"
                FROM "Players" p
                WHERE p."Id" = rp."PlayerId";
                """);

            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "PlayerAnswers",
                newName: "RoomPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerAnswers_PlayerId",
                table: "PlayerAnswers",
                newName: "IX_PlayerAnswers_RoomPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerAnswers_MatchQuestionId_PlayerId",
                table: "PlayerAnswers",
                newName: "IX_PlayerAnswers_MatchQuestionId_RoomPlayerId");

            // ответ привязываем к участию игрока в комнате матча
            migrationBuilder.Sql("""
                UPDATE "PlayerAnswers" pa
                SET "RoomPlayerId" = rp."Id"
                FROM "MatchQuestions" mq
                JOIN "Matches" m ON m."Id" = mq."MatchId"
                JOIN "RoomParticipations" rp ON rp."RoomId" = m."RoomId"
                WHERE mq."Id" = pa."MatchQuestionId"
                  AND rp."PlayerId" = pa."RoomPlayerId";
                """);

            // ответы не участников комнаты привязать не к чему
            migrationBuilder.Sql("""
                DELETE FROM "PlayerAnswers" pa
                WHERE NOT EXISTS (
                    SELECT 1 FROM "RoomParticipations" rp WHERE rp."Id" = pa."RoomPlayerId");
                """);

            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_PlayerId",
                table: "RoomParticipations");

            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_RoomId_PlayerId",
                table: "RoomParticipations");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "RoomParticipations");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeycloakSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_RoomId_Nickname",
                table: "RoomParticipations",
                columns: new[] { "RoomId", "Nickname" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_RoomId_UserId",
                table: "RoomParticipations",
                columns: new[] { "RoomId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_UserId",
                table: "RoomParticipations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_KeycloakSubject",
                table: "Users",
                column: "KeycloakSubject",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerAnswers_RoomParticipations_RoomPlayerId",
                table: "PlayerAnswers",
                column: "RoomPlayerId",
                principalTable: "RoomParticipations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoomParticipations_Users_UserId",
                table: "RoomParticipations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerAnswers_RoomParticipations_RoomPlayerId",
                table: "PlayerAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_RoomParticipations_Users_UserId",
                table: "RoomParticipations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_RoomId_Nickname",
                table: "RoomParticipations");

            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_RoomId_UserId",
                table: "RoomParticipations");

            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_UserId",
                table: "RoomParticipations");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "RoomParticipations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "RoomParticipations");

            migrationBuilder.RenameColumn(
                name: "RoomPlayerId",
                table: "PlayerAnswers",
                newName: "PlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerAnswers_RoomPlayerId",
                table: "PlayerAnswers",
                newName: "IX_PlayerAnswers_PlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerAnswers_MatchQuestionId_RoomPlayerId",
                table: "PlayerAnswers",
                newName: "IX_PlayerAnswers_MatchQuestionId_PlayerId");

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "RoomParticipations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_PlayerId",
                table: "RoomParticipations",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_RoomId_PlayerId",
                table: "RoomParticipations",
                columns: new[] { "RoomId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_UserName",
                table: "Players",
                column: "UserName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerAnswers_Players_PlayerId",
                table: "PlayerAnswers",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoomParticipations_Players_PlayerId",
                table: "RoomParticipations",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
