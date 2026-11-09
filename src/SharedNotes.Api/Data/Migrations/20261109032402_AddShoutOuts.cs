using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharedNotes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShoutOuts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShoutOutId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShoutOuts",
                columns: table => new
                {
                    ShoutOutId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenderEmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    NoteId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByEmployeeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Response = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoutOuts", x => x.ShoutOutId);
                    table.ForeignKey(
                        name: "FK_ShoutOuts_Employees_ResolvedByEmployeeId",
                        column: x => x.ResolvedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShoutOuts_Employees_SenderEmployeeId",
                        column: x => x.SenderEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShoutOuts_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "NoteId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShoutOuts_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ShoutOutId",
                table: "Notifications",
                column: "ShoutOutId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoutOuts_NoteId",
                table: "ShoutOuts",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoutOuts_ResolvedByEmployeeId",
                table: "ShoutOuts",
                column: "ResolvedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoutOuts_SenderEmployeeId_CreatedAt",
                table: "ShoutOuts",
                columns: new[] { "SenderEmployeeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShoutOuts_TeamId_ResolvedAt",
                table: "ShoutOuts",
                columns: new[] { "TeamId", "ResolvedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_ShoutOuts_ShoutOutId",
                table: "Notifications",
                column: "ShoutOutId",
                principalTable: "ShoutOuts",
                principalColumn: "ShoutOutId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_ShoutOuts_ShoutOutId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "ShoutOuts");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ShoutOutId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ShoutOutId",
                table: "Notifications");
        }
    }
}
