using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOneActiveMatchPerRoomIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_RoomId",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_RoomId",
                table: "Matches",
                column: "RoomId",
                unique: true,
                filter: "\"Status\" <> 'Finished'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_RoomId",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_RoomId",
                table: "Matches",
                column: "RoomId");
        }
    }
}
