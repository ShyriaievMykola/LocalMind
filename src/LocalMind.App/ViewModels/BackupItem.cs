using System.Globalization;
using LocalMind.Core.Backup.Dtos;

namespace LocalMind.App.ViewModels;

/// <summary>Бекап у списку: дата і розмір для показу.</summary>
/// <param name="Info">Дані сервера.</param>
public sealed record BackupItem(BackupInfo Info)
{
    public Guid Id => Info.Id;

    public string CreatedText => Info.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public string SizeText => FormatSize(Info.SizeBytes);

    /// <summary>Розмір зашифрованого файла в зручних одиницях.</summary>
    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} КБ",
        _ => $"{(bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} МБ",
    };
}
