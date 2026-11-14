using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMind.Core.Storage.Migrations
{
    /// <summary>
    /// CHECK-обмеження: невід'ємні <c>Ordinal</c> і <c>StartOffset</c>, статус документа з переліку, непорожній вектор
    /// з цілою кількістю float32, непорожні назва моделі й шлях папки. SQLite не вміє додавати обмеження до наявної таблиці,
    /// тому EF перебудовує таблицю (створює нову, копіює дані, видаляє стару, перейменовує). Це зачіпає FTS5:
    /// тригери таблиці <c>Chunks</c> зникають разом зі старою таблицею, а rowid у новій можуть бути іншими;
    /// це виправляє наступна міграція <see cref="RestoreChunksFts"/> (EF ставить перебудову таблиць наприкінці
    /// своїх операцій, тому SQL у цій самій міграції виконався б раніше за неї).
    /// </summary>
    public partial class AddCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: StorageChecks.ChunkOrdinalName, table: "Chunks", sql: StorageChecks.ChunkOrdinal);
            migrationBuilder.AddCheckConstraint(
                name: StorageChecks.ChunkStartOffsetName, table: "Chunks", sql: StorageChecks.ChunkStartOffset);
            migrationBuilder.AddCheckConstraint(
                name: StorageChecks.DocumentStatusName, table: "Documents", sql: StorageChecks.DocumentStatus);
            migrationBuilder.AddCheckConstraint(
                name: StorageChecks.EmbeddingVectorName, table: "Embeddings", sql: StorageChecks.EmbeddingVector);
            migrationBuilder.AddCheckConstraint(
                name: StorageChecks.EmbeddingModelNameName, table: "EmbeddingModels", sql: StorageChecks.EmbeddingModelName);
            migrationBuilder.AddCheckConstraint(
                name: StorageChecks.SourceFolderPathName, table: "SourceFolders", sql: StorageChecks.SourceFolderPath);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "SQLite не вміє прибирати CHECK-обмеження міграцією: для відкоту відновіть базу з копії до оновлення.");
    }
}
