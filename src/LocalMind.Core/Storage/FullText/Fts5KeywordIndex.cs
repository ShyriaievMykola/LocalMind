using System.Data.Common;
using System.Globalization;
using System.Text;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Core.Storage.FullText;

/// <summary>
/// Keyword-пошук через SQLite FTS5 з ранжуванням BM25. Таблицю <c>ChunksFts</c> синхронізують тригери
/// (міграція <c>AddChunksFts</c>), тому фрагменти видно одразу після збереження.
/// Запит користувача ніколи не потрапляє в SQL: він передається параметром, а кожне слово береться в лапки —
/// синтаксис FTS5 (<c>"</c>, <c>*</c>, <c>OR</c>, <c>NEAR</c>…) не спрацьовує.
/// </summary>
public sealed class Fts5KeywordIndex(IDbContextFactory<LocalMindDbContext> contextFactory) : IKeywordIndex
{
    // bm25() менший — краще; повертаємо зі знаком мінус, щоб більша оцінка була кращою.
    private const string SearchSql = """
        SELECT c.Id, -bm25(ChunksFts) AS Score
        FROM ChunksFts
        JOIN Chunks AS c ON c.rowid = ChunksFts.rowid
        WHERE ChunksFts MATCH $match
        ORDER BY bm25(ChunksFts)
        LIMIT $limit
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(string query, int k, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var match = BuildMatchExpression(query);
        if (match is null)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = SearchSql;
        AddParameter(command, "$match", match);
        AddParameter(command, "$limit", k);

        var results = new List<ScoredChunk>(k);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            results.Add(new ScoredChunk(id, reader.GetDouble(1)));
        }

        return results;
    }

    /// <summary>
    /// Вираз FTS5: кожне слово запиту — рядок у лапках з префіксним пошуком за основою слова (<c>"кав"*</c>),
    /// слова об'єднані через OR, а BM25 ставить вище фрагменти, де збіглося більше слів.
    /// <see langword="null"/>, якщо значущих слів немає.
    /// </summary>
    internal static string? BuildMatchExpression(string query)
    {
        var terms = SearchTerms.Split(query);
        if (terms.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            if (builder.Length > 0)
            {
                builder.Append(" OR ");
            }

            // Лапки всередині рядка FTS5 подвоюються. Після Split їх у слові немає, але це остання лінія захисту.
            builder.Append('"')
                .Append(SearchTerms.Stem(term).Replace("\"", "\"\"", StringComparison.Ordinal))
                .Append("\"*");
        }

        return builder.ToString();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
