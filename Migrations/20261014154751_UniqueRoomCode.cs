using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class UniqueRoomCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Rooms_RoomCode",
                table: "Rooms",
                column: "RoomCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rooms_RoomCode",
                table: "Rooms");
        }
    }
}
