using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SharedNotes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoteRevisions",
                columns: table => new
                {
                    RevisionId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TeamName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Topics = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NoteVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                    EditedByEmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteRevisions", x => x.RevisionId);
                    table.ForeignKey(
                        name: "FK_NoteRevisions_Employees_EditedByEmployeeId",
                        column: x => x.EditedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoteRevisions_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "NoteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "NoteRevisions",
                columns: new[] { "RevisionId", "Action", "Content", "EditedAt", "EditedByEmployeeId", "NoteId", "NoteVersion", "TeamName", "Title", "Topics", "Visibility" },
                values: new object[,]
                {
                    { 1, "Created", "Review PRs, update docs, book travel.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 1, 1, new Guid("00000000-0000-0000-0000-000000000001"), null, "My private to-do list", "Planning", "Private" },
                    { 2, "Created", "Goals for the next sprint: finish the notes API and write tests.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 2, 2, new Guid("00000000-0000-0000-0000-000000000002"), "Engineering", "Sprint planning", "Meeting Notes, Planning", "Team" },
                    { 3, "Created", "The office is closed on public holidays and the last Friday of each quarter.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 3, 3, new Guid("00000000-0000-0000-0000-000000000003"), null, "Company holiday schedule", "", "All" },
                    { 4, "Created", "Spring launch: short videos, customer stories, partner webinar.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 3, 4, new Guid("00000000-0000-0000-0000-000000000004"), "Marketing", "Campaign ideas", "Ideas", "Team" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteRevisions_EditedByEmployeeId",
                table: "NoteRevisions",
                column: "EditedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteRevisions_NoteId",
                table: "NoteRevisions",
                column: "NoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoteRevisions");
        }
    }
}
