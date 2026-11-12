using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LocalMind.App.ViewModels;

namespace LocalMind.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Перетягування файлів і папок у будь-яке місце вікна (A-11).
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Що саме тягнуть, перевіряємо під час Drop: не всі ОС віддають список файлів до відпускання.
        e.DragEffects = DragDropEffects.Copy;
        if (ViewModel is { } viewModel)
        {
            viewModel.IsDragOver = true;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave приходить і при переході між елементами всередині вікна — ховаємо підказку,
        // лише коли курсор справді вийшов за межі вікна.
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
        {
            return;
        }

        if (ViewModel is { } viewModel)
        {
            viewModel.IsDragOver = false;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        List<string> paths = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .ToList() ?? [];

        _ = viewModel.DropPathsCommand.ExecuteAsync(paths);
    }
}