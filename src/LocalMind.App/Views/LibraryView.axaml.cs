using Avalonia;
using Avalonia.Controls;
using LocalMind.App.ViewModels;

namespace LocalMind.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Щоразу, коли користувач відкриває сторінку, показуємо актуальний стан бібліотеки.
        if (DataContext is LibraryViewModel viewModel)
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }
}