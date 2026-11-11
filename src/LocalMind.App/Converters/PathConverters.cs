using Avalonia.Data.Converters;

namespace LocalMind.App.Converters;

/// <summary>Назва файла для простого списку результатів; повний шлях показується окремо.</summary>
public static class PathConverters
{
    public static FuncValueConverter<string?, string> FileName { get; } =
        new(path => Path.GetFileName(path) ?? string.Empty);
}
