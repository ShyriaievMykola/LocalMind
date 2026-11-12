using Avalonia;
using Avalonia.Controls;
using LocalMind.App.ViewModels;

namespace LocalMind.App.Views;

public partial class AccountView : UserControl
{
    public AccountView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Щоразу, коли користувач відкриває сторінку, показуємо актуальний стан сесії.
        if (DataContext is AccountViewModel viewModel)
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }
}
