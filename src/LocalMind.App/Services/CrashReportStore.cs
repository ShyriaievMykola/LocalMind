using LocalMind.Core.Diagnostics;

namespace LocalMind.App.Services;

/// <summary>Тека журналів і звітів про аварії застосунку: <c>logs/</c> поруч із налаштуваннями користувача.</summary>
public sealed class CrashReportStore(string directory)
{
    /// <summary>Тека журналів (<c>app-*.log</c>) і звітів (<c>crash-*.txt</c>).</summary>
    public string Directory { get; } = directory;

    /// <summary>Записує звіт про аварію; повертає шлях або <see langword="null"/>, якщо записати не вдалося.</summary>
    public string? TryWrite(Exception exception, string source) => CrashReport.TryWrite(Directory, exception, source);
}
