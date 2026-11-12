namespace LocalMind.Core;

/// <summary>
/// Єдине правило порівняння шляхів для індексації, стеження і сховища. Windows і macOS за замовчуванням
/// не розрізняють регістр у шляхах (<c>C:\Notes</c> і <c>c:\notes</c> — одна папка), Linux розрізняє.
/// </summary>
public static class FilePaths
{
    /// <summary>Чи ігнорувати регістр на поточній ОС.</summary>
    public static bool IgnoreCase { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>Порівнювач для словників і множин шляхів.</summary>
    public static StringComparer Comparer { get; } = IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Спосіб порівняння для <c>Equals</c> / <c>StartsWith</c>.</summary>
    public static StringComparison Comparison { get; } = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Чи однакові шляхи.</summary>
    public static bool AreSame(string first, string second) => string.Equals(first, second, Comparison);

    /// <summary>
    /// Чи збігається шлях з папкою або лежить усередині неї. Приймає обидва роздільники; <paramref name="folder"/> —
    /// без роздільника в кінці, крім кореня (<c>/</c>, <c>C:\</c>).
    /// </summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrEmpty(folder);

        return path.Equals(folder, Comparison)
            || (path.StartsWith(folder, Comparison)
                && (folder[^1] is '/' or '\\' || path[folder.Length] is '/' or '\\'));
    }
}
