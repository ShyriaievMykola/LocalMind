using Avalonia;
using Avalonia.Controls;
using LocalMind.App.ViewModels;

namespace LocalMind.App.Views;

public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Щоразу, коли користувач відкриває сторінку, показуємо актуальний стан сесії і список бекапів.
        if (DataContext is BackupViewModel viewModel)
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }
}
