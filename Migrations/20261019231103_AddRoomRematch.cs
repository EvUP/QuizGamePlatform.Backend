using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGamePlatform.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomRematch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameNumber",
                table: "Rooms",
                type: "integer",
                nullable: false,
                // все комнаты до переигровок первые в своей серии
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousRoomId",
                table: "Rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "Rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginRoomPlayerId",
                table: "RoomParticipations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "CategoryIds",
                table: "Matches",
                type: "uuid[]",
                nullable: false,
                // у сыгранных матчей настроек нет, следующей игре подставлять нечего
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "RequestedQuestionCount",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_PreviousRoomId",
                table: "Rooms",
                column: "PreviousRoomId",
                unique: true,
                filter: "\"PreviousRoomId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_SeriesId_GameNumber",
                table: "Rooms",
                columns: new[] { "SeriesId", "GameNumber" },
                unique: true,
                filter: "\"SeriesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoomParticipations_RoomId_OriginRoomPlayerId",
                table: "RoomParticipations",
                columns: new[] { "RoomId", "OriginRoomPlayerId" },
                unique: true,
                filter: "\"OriginRoomPlayerId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Rooms_PreviousRoomId",
                table: "Rooms",
                column: "PreviousRoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Rooms_PreviousRoomId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_PreviousRoomId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_SeriesId_GameNumber",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_RoomParticipations_RoomId_OriginRoomPlayerId",
                table: "RoomParticipations");

            migrationBuilder.DropColumn(
                name: "GameNumber",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "PreviousRoomId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "OriginRoomPlayerId",
                table: "RoomParticipations");

            migrationBuilder.DropColumn(
                name: "CategoryIds",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RequestedQuestionCount",
                table: "Matches");
        }
    }
}
