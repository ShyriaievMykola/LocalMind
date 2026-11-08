namespace LocalMind.Core.Ingestion.Watching;

/// <summary>Зміна, яку <see cref="FolderWatcher"/> ставить у чергу індексації.</summary>
/// <param name="Path">Повний шлях до файла (<see cref="FolderChangeKind.FileChanged"/>) або папки
/// (<see cref="FolderChangeKind.RescanRequired"/>).</param>
/// <param name="Kind">Що треба зробити з цим шляхом.</param>
public sealed record FolderChange(string Path, FolderChangeKind Kind);