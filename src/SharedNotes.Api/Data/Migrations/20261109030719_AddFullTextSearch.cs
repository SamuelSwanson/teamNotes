using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharedNotes.Api.Data.Migrations
{
    /// <summary>
    /// Adds an SQLite FTS5 index over Notes(Title, Content). It is an "external content" index: the text lives only in
    /// Notes, and triggers keep the index in step with every insert/update/delete (inside the same transaction).
    /// The porter tokenizer gives stemming ("plan" matches "planning"); unicode61 folds case and diacritics.
    /// NOTE: if a future migration makes EF rebuild the Notes table (SQLite cannot alter some columns in place),
    /// the triggers are dropped with it and must be recreated in that migration.
    /// </summary>
    public partial class AddFullTextSearch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE NotesFts USING fts5(
                    Title, Content,
                    content='Notes', content_rowid='NoteId',
                    tokenize='porter unicode61 remove_diacritics 2');
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER Notes_fts_ai AFTER INSERT ON Notes BEGIN
                    INSERT INTO NotesFts(rowid, Title, Content) VALUES (new.NoteId, new.Title, new.Content);
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER Notes_fts_ad AFTER DELETE ON Notes BEGIN
                    INSERT INTO NotesFts(NotesFts, rowid, Title, Content) VALUES ('delete', old.NoteId, old.Title, old.Content);
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER Notes_fts_au AFTER UPDATE ON Notes BEGIN
                    INSERT INTO NotesFts(NotesFts, rowid, Title, Content) VALUES ('delete', old.NoteId, old.Title, old.Content);
                    INSERT INTO NotesFts(rowid, Title, Content) VALUES (new.NoteId, new.Title, new.Content);
                END;
                """);
            // Index the notes that already exist (e.g. the seed data).
            migrationBuilder.Sql("INSERT INTO NotesFts(NotesFts) VALUES ('rebuild');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Notes_fts_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Notes_fts_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Notes_fts_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS NotesFts;");
        }
    }
}
