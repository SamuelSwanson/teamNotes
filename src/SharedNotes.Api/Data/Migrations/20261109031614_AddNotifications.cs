using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharedNotes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipientEmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorEmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NoteId = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notifications_Employees_ActorEmployeeId",
                        column: x => x.ActorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Employees_RecipientEmployeeId",
                        column: x => x.RecipientEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "NoteId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Notifications_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ActorEmployeeId",
                table: "Notifications",
                column: "ActorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_NoteId",
                table: "Notifications",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientEmployeeId_ReadAt_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientEmployeeId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TeamId",
                table: "Notifications",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
