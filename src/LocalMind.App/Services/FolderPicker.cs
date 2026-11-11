using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace LocalMind.App.Services;

/// <summary>Вибір папки через <see cref="IStorageProvider"/> головного вікна Avalonia.</summary>
internal sealed class FolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            // null означає «користувач скасував»; відсутнє вікно — це помилка, і VM має її показати.
            throw new InvalidOperationException("Головне вікно ще не відкрите — немає де показати вибір папки.");
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Оберіть папку з нотатками",
            AllowMultiple = false,
        });

        // TryGetLocalPath повертає null для віртуальних розташувань, яких немає на диску.
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}