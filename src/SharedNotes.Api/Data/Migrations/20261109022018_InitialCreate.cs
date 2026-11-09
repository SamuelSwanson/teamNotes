using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SharedNotes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.EmployeeId);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.TeamId);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.TopicId);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    NoteId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    CreatedByEmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedByEmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.NoteId);
                    table.CheckConstraint("CK_Notes_TeamId_Visibility", "(\"Visibility\" = 'Team' AND \"TeamId\" IS NOT NULL) OR (\"Visibility\" <> 'Team' AND \"TeamId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Notes_Employees_CreatedByEmployeeId",
                        column: x => x.CreatedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notes_Employees_UpdatedByEmployeeId",
                        column: x => x.UpdatedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notes_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => new { x.EmployeeId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_TeamMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoteTopics",
                columns: table => new
                {
                    NoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTopics", x => new { x.NoteId, x.TopicId });
                    table.ForeignKey(
                        name: "FK_NoteTopics_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "NoteId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoteTopics_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "TopicId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Employees",
                columns: new[] { "EmployeeId", "CreatedAt", "Email", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), "alice@example.com", "Alice Chen" },
                    { 2, new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), "bob@example.com", "Bob Martinez" },
                    { 3, new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), "carol@example.com", "Carol Singh" },
                    { 4, new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), "dave@example.com", "Dave Okafor" }
                });

            migrationBuilder.InsertData(
                table: "Teams",
                columns: new[] { "TeamId", "CreatedAt", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), "Engineering" },
                    { 2, new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), "Marketing" }
                });

            migrationBuilder.InsertData(
                table: "Topics",
                columns: new[] { "TopicId", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "Roadmaps and priorities", "Planning" },
                    { 2, "Minutes and action items", "Meeting Notes" },
                    { 3, null, "Ideas" }
                });

            migrationBuilder.InsertData(
                table: "Notes",
                columns: new[] { "NoteId", "Content", "CreatedAt", "CreatedByEmployeeId", "TeamId", "Title", "UpdatedAt", "UpdatedByEmployeeId", "Version", "Visibility" },
                values: new object[,]
                {
                    { 1, "Review PRs, update docs, book travel.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 1, null, "My private to-do list", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 1, new Guid("00000000-0000-0000-0000-000000000001"), "Private" },
                    { 2, "Goals for the next sprint: finish the notes API and write tests.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 2, 1, "Sprint planning", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 2, new Guid("00000000-0000-0000-0000-000000000002"), "Team" },
                    { 3, "The office is closed on public holidays and the last Friday of each quarter.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 3, null, "Company holiday schedule", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 3, new Guid("00000000-0000-0000-0000-000000000003"), "All" },
                    { 4, "Spring launch: short videos, customer stories, partner webinar.", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 3, 2, "Campaign ideas", new DateTime(2026, 1, 5, 9, 0, 0, 0, DateTimeKind.Utc), 3, new Guid("00000000-0000-0000-0000-000000000004"), "Team" }
                });

            migrationBuilder.InsertData(
                table: "TeamMembers",
                columns: new[] { "EmployeeId", "TeamId", "Role" },
                values: new object[,]
                {
                    { 1, 1, "Lead" },
                    { 1, 2, null },
                    { 2, 1, null },
                    { 3, 2, "Lead" }
                });

            migrationBuilder.InsertData(
                table: "NoteTopics",
                columns: new[] { "NoteId", "TopicId" },
                values: new object[,]
                {
                    { 1, 1 },
                    { 2, 1 },
                    { 2, 2 },
                    { 4, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                table: "Employees",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_CreatedByEmployeeId",
                table: "Notes",
                column: "CreatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_TeamId",
                table: "Notes",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UpdatedAt",
                table: "Notes",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UpdatedByEmployeeId",
                table: "Notes",
                column: "UpdatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteTopics_TopicId",
                table: "NoteTopics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId",
                table: "TeamMembers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_Name",
                table: "Topics",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoteTopics");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "Topics");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
