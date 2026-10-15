using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTeamGame",
                table: "Rooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TeamNumber",
                table: "RoomParticipations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoomTeams",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomTeams", x => new { x.RoomId, x.Number });
                    table.ForeignKey(
                        name: "FK_RoomTeams_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTeams_RoomId_Name",
                table: "RoomTeams",
                columns: new[] { "RoomId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomTeams");

            migrationBuilder.DropColumn(
                name: "IsTeamGame",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "TeamNumber",
                table: "RoomParticipations");
        }
    }
}
