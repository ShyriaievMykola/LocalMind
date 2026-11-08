namespace LocalMind.Core.Storage.Entities;

/// <summary>Відстежувана папка.</summary>
public sealed class SourceFolderEntity
{
    /// <summary>Шлях до папки без роздільника в кінці; первинний ключ.</summary>
    public required string Path { get; set; }
}
