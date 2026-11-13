using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMind.Core.Storage.Migrations
{
    /// <summary>
    /// Відновлює повнотекстовий пошук після перебудови таблиці <c>Chunks</c> міграцією <c>AddCheckConstraints</c>:
    /// тригери старої таблиці зникли, а rowid фрагментів змінилися, тож індекс <c>ChunksFts</c> (external content,
    /// зв'язок за rowid) перебудовується. Модель EF не змінюється.
    /// </summary>
    [DbContext(typeof(LocalMindDbContext))]
    [Migration("20261113200100_RestoreChunksFts")]
    public partial class RestoreChunksFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Chunks_AfterInsert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Chunks_AfterDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Chunks_AfterUpdate;");

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

            migrationBuilder.Sql("INSERT INTO ChunksFts(ChunksFts) VALUES ('rebuild');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Тригери й індекс належать міграції AddChunksFts; відкат — разом із нею.
        }
    }
}
