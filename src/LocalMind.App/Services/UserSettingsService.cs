using System.Text.Json;
using System.Text.Json.Nodes;
using LocalMind.Core.Configuration;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Options;

namespace LocalMind.App.Services;

/// <summary>
/// Зберігає налаштування користувача в JSON у форматі конфігурації (<c>LocalMind:Search:DefaultTopK</c> тощо).
/// <see cref="AppHost"/> додає цей файл поверх <c>appsettings.json</c>, тож після перезапуску ядро отримує
/// нові значення через звичайні Options і нічого не знає про сторінку налаштувань.
/// </summary>
internal sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SearchOptions _search;
    private readonly ChunkingOptions _chunking;
    private readonly IEnumerable<IValidateOptions<SearchOptions>> _searchValidators;
    private readonly IEnumerable<IValidateOptions<ChunkingOptions>> _chunkingValidators;
    private readonly UserSettingsFile _file;

    public UserSettingsService(
        IOptions<AppOptions> app,
        IOptions<SearchOptions> search,
        IOptions<ChunkingOptions> chunking,
        IOptions<EmbeddingModelOptions> model,
        IEnumerable<IValidateOptions<SearchOptions>> searchValidators,
        IEnumerable<IValidateOptions<ChunkingOptions>> chunkingValidators,
        UserSettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(chunking);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(searchValidators);
        ArgumentNullException.ThrowIfNull(chunkingValidators);
        ArgumentNullException.ThrowIfNull(file);

        // Якщо конфігурація некоректна, ядро вже повідомило CoreStatus.Failed — сторінка все одно має відкритися.
        _search = Read(search, new SearchOptions());
        _chunking = Read(chunking, new ChunkingOptions());
        _searchValidators = searchValidators;
        _chunkingValidators = chunkingValidators;
        _file = file;

        Current = new UserSettings(
            Read(app, new AppOptions()).Theme,
            _search.DefaultMode,
            _search.DefaultTopK,
            Read(model, new EmbeddingModelOptions()).ModelPath,
            _chunking.ChunkSizeTokens,
            _chunking.OverlapPercent);
    }

    public UserSettings Current { get; }

    public IReadOnlyList<string> Validate(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();
        if (!Enum.IsDefined(settings.Theme))
        {
            errors.Add("Невідома тема.");
        }

        // Значення, яких сторінка не змінює (CandidatesPerIndex, стратегія нарізки), беремо з поточних налаштувань.
        var search = new SearchOptions
        {
            DefaultTopK = settings.DefaultTopK,
            DefaultMode = settings.DefaultSearchMode,
            CandidatesPerIndex = _search.CandidatesPerIndex,
            RrfK = _search.RrfK,
        };
        errors.AddRange(Run(_searchValidators, search));

        var chunking = new ChunkingOptions
        {
            Strategy = _chunking.Strategy,
            ChunkSizeTokens = settings.ChunkSizeTokens,
            OverlapPercent = settings.OverlapPercent,
        };
        errors.AddRange(Run(_chunkingValidators, chunking));

        if (string.IsNullOrWhiteSpace(settings.ModelPath))
        {
            errors.Add("Вкажіть шлях до файла моделі.");
        }
        else if (!File.Exists(settings.ModelPath))
        {
            errors.Add($"Файл моделі не знайдено: {settings.ModelPath}");
        }

        return errors;
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        var errors = Validate(settings);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
        }

        var root = new JsonObject();
        SetSection(root, AppOptions.SectionName, new JsonObject
        {
            ["Theme"] = Enum.GetName(settings.Theme),
        });
        SetSection(root, SearchOptions.SectionName, new JsonObject
        {
            ["DefaultTopK"] = settings.DefaultTopK,
            ["DefaultMode"] = Enum.GetName(settings.DefaultSearchMode),
        });
        SetSection(root, ChunkingOptions.SectionName, new JsonObject
        {
            ["ChunkSizeTokens"] = settings.ChunkSizeTokens,
            ["OverlapPercent"] = settings.OverlapPercent,
        });
        SetSection(root, EmbeddingModelOptions.SectionName, new JsonObject
        {
            ["ModelPath"] = settings.ModelPath,
        });

        // Спершу тимчасовий файл, потім заміна: обірваний запис не залишить зіпсованих налаштувань.
        Directory.CreateDirectory(Path.GetDirectoryName(_file.Path)!);
        var temporary = _file.Path + ".tmp";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(WriteOptions), cancellationToken);
        File.Move(temporary, _file.Path, overwrite: true);
    }

    private static T Read<T>(IOptions<T> options, T fallback)
        where T : class
    {
        try
        {
            return options.Value;
        }
        catch (OptionsValidationException)
        {
            return fallback;
        }
    }

    private static IEnumerable<string> Run<T>(IEnumerable<IValidateOptions<T>> validators, T options)
        where T : class =>
        validators
            .Select(validator => validator.Validate(Options.DefaultName, options))
            .Where(result => result.Failed)
            .SelectMany(result => result.Failures ?? new[] { result.FailureMessage ?? "Некоректне значення." });

    /// <summary>Кладе значення за шляхом секції <c>LocalMind:Search</c> → <c>{"LocalMind":{"Search":{…}}}</c>.</summary>
    private static void SetSection(JsonObject root, string sectionPath, JsonObject values)
    {
        var keys = sectionPath.Split(':');
        var node = root;
        foreach (var key in keys[..^1])
        {
            if (node[key] is not JsonObject child)
            {
                child = new JsonObject();
                node[key] = child;
            }

            node = child;
        }

        node[keys[^1]] = values;
    }
}