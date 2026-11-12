using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalMind.App.Services;

/// <summary>
/// Читання й запис файла налаштувань користувача. Файл спільний для кількох сервісів
/// (<see cref="UserSettingsService"/>, <see cref="BackupServerSettings"/>), тому кожен змінює лише свої секції,
/// а решту лишає як є.
/// </summary>
internal static class UserSettingsJson
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // Ті самі правила, що й у JSON-провайдера конфігурації: коментарі й кінцеві коми дозволені.
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Запис «прочитати → змінити → записати» не повинен перетинатися з іншим таким записом.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Змінює файл: <paramref name="change"/> отримує поточний вміст (порожній, якщо файла немає чи він пошкоджений).</summary>
    public static async Task UpdateAsync(string path, Action<JsonObject> change, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var root = Load(path);
            change(root);

            // Спершу тимчасовий файл, потім заміна: обірваний запис не залишить зіпсованих налаштувань.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, root.ToJsonString(WriteOptions), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Секція за шляхом на кшталт <c>LocalMind:Backup</c>; відсутні вузли створюються.</summary>
    public static JsonObject Section(JsonObject root, string sectionPath)
    {
        var node = root;
        foreach (var key in sectionPath.Split(':'))
        {
            if (node[key] is not JsonObject child)
            {
                child = new JsonObject();
                node[key] = child;
            }

            node = child;
        }

        return node;
    }

    /// <summary>
    /// Кладе значення в секцію <c>LocalMind:Search</c> → <c>{"LocalMind":{"Search":{…}}}</c> по ключах:
    /// ключі, яких немає в <paramref name="values"/> (наприклад, задані вручну <c>RrfK</c>), лишаються.
    /// </summary>
    public static void SetSection(JsonObject root, string sectionPath, JsonObject values)
    {
        var section = Section(root, sectionPath);
        foreach (var (key, value) in values)
        {
            section[key] = value?.DeepClone();
        }
    }

    /// <summary>
    /// Поточний вміст файла. Пошкоджений файл не губимо мовчки: його копія лишається поруч із розширенням
    /// <c>.bad</c> (попередня копія замінюється), а запис іде з порожнього об'єкта. Помилки доступу до файла
    /// піднімаються до викликача — він показує їх користувачу.
    /// </summary>
    private static JsonObject Load(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".bad", overwrite: true);
            return new JsonObject();
        }
    }
}
