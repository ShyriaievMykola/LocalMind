using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMind.Core.Storage.Migrations
{
    /// <summary>
    /// Повнотекстовий індекс фрагментів (FTS5). Таблиця з external content: текст не дублюється, а береться
    /// з <c>Chunks</c> за rowid; тригери тримають індекс в актуальному стані. Модель EF не змінюється.
    /// </summary>
    [DbContext(typeof(LocalMindDbContext))]
    [Migration("20261111120000_AddChunksFts")]
    public partial class AddChunksFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // unicode61 розбиває на слова будь-якою мовою і знімає регістр (зокрема кирилиці).
            // remove_diacritics 0: інакше «й» і «ї» могли б злитися з «и» та «і».
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE ChunksFts USING fts5(
                    Text,
                    content = 'Chunks',
                    content_rowid = 'rowid',
                    tokenize = 'unicode61 remove_diacritics 0'
                );
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER Chunks_AfterInsert AFTER INSERT ON Chunks BEGIN
                    INSERT INTO ChunksFts(rowid, Text) VALUES (new.rowid, new.Text);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER Chunks_AfterDelete AFTER DELETE ON Chunks BEGIN
                    INSERT INTO ChunksFts(ChunksFts, rowid, Text) VALUES ('delete', old.rowid, old.Text);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER Chunks_AfterUpdate AFTER UPDATE OF Text ON Chunks BEGIN
                    INSERT INTO ChunksFts(ChunksFts, rowid, Text) VALUES ('delete', old.rowid, old.Text);
                    INSERT INTO ChunksFts(rowid, Text) VALUES (new.rowid, new.Text);
                END;
                """);

            // Фрагменти, збережені до цієї міграції.
            migrationBuilder.Sql("INSERT INTO ChunksFts(ChunksFts) VALUES ('rebuild');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Chunks_AfterUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Chunks_AfterDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Chunks_AfterInsert;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS ChunksFts;");
        }
    }
}
