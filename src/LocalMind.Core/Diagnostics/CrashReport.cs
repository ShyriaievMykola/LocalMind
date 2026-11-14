using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalMind.Core.Diagnostics;

/// <summary>
/// Звіт про аварію: окремий файл <c>crash-yyyyMMdd-HHmmss-fff.txt</c> з версією програми, ОС, джерелом і повним
/// винятком. Пишеться лише для необроблених винятків, тож порожня тека означає, що аварій не було. Звіт лишається на
/// диску користувача й нікуди не відправляється.
/// </summary>
public static class CrashReport
{
    /// <summary>Записує звіт і повертає шлях до файла, або <see langword="null"/>, якщо записати не вдалося.</summary>
    /// <param name="directory">Тека звітів.</param>
    /// <param name="exception">Необроблений виняток.</param>
    /// <param name="source">Звідки виняток: UI-потік, фонове завдання, команда CLI.</param>
    /// <param name="timeProvider">Годинник; тести передають власний.</param>
    public static string? TryWrite(string directory, Exception exception, string source, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var now = (timeProvider ?? TimeProvider.System).GetLocalNow();
        var name = string.Create(CultureInfo.InvariantCulture, $"crash-{now:yyyyMMdd-HHmmss-fff}");

        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + ".txt");
            // Два звіти в одну мілісекунду (виняток у кількох потоках) не перезаписують один одного.
            for (var attempt = 2; File.Exists(path); attempt++)
            {
                path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{name}-{attempt}.txt"));
            }

            File.WriteAllText(path, Build(exception, source, now), Encoding.UTF8);
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Текст звіту.</summary>
    internal static string Build(Exception exception, string source, DateTimeOffset time)
    {
        var entry = Assembly.GetEntryAssembly();
        var version = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entry?.GetName().Version?.ToString()
            ?? "невідомо";

        var builder = new StringBuilder();
        builder.AppendLine("LocalMind: звіт про аварію");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Час:       {time:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Програма:  {entry?.GetName().Name ?? "невідомо"} {version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ОС:        {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        builder.AppendLine(CultureInfo.InvariantCulture, $".NET:      {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Джерело:   {source}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }
}
