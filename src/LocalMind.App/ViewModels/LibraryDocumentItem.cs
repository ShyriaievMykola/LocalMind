using LocalMind.Core.Models;

namespace LocalMind.App.ViewModels;

/// <summary>Рядок списку документів бібліотеки.</summary>
/// <param name="Path">Повний шлях до файла.</param>
/// <param name="Status">Результат індексації.</param>
/// <param name="ChunkCount">Скільки фрагментів збережено.</param>
/// <param name="Error">Причина помилки для <see cref="DocumentStatus.Error"/>, інакше <see langword="null"/>.</param>
public sealed record LibraryDocumentItem(string Path, DocumentStatus Status, int ChunkCount, string? Error)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public bool IsError => Status == DocumentStatus.Error;

    public string StatusText => Status switch
    {
        DocumentStatus.Indexed => "Проіндексовано",
        DocumentStatus.Empty => "Порожній файл",
        DocumentStatus.NoText => "Текст не знайдено",
        DocumentStatus.Error => "Помилка",
        _ => "Невідомо",
    };
}