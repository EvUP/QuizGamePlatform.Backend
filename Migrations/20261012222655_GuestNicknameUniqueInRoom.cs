using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class GuestNicknameUniqueInRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_RoomId_Nickname",
                table: "RoomParticipations");

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_RoomId_Nickname",
                table: "RoomParticipations",
                columns: new[] { "RoomId", "Nickname" },
                unique: true,
                filter: "\"UserId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_RoomId_Nickname",
                table: "RoomParticipations");

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_RoomId_Nickname",
                table: "RoomParticipations",
                columns: new[] { "RoomId", "Nickname" },
                unique: true);
        }
    }
}
