using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostUserId",
                table: "Rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_HostUserId",
                table: "Rooms",
                column: "HostUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Users_HostUserId",
                table: "Rooms",
                column: "HostUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Users_HostUserId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_HostUserId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "HostUserId",
                table: "Rooms");
        }
    }
}
