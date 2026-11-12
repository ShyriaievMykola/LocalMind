using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LocalMind.App.ViewModels;

namespace LocalMind.App.Views;

public partial class SearchView : UserControl
{
    private SearchViewModel? _viewModel;

    public SearchView()
    {
        InitializeComponent();

        // Tunnel: стрілки, Enter і Esc перехоплюємо раніше, ніж їх обробить сам TextBox.
        QueryBox.AddHandler(KeyDownEvent, OnQueryKeyDown, RoutingStrategies.Tunnel);
        // Фокус у полі (клік, Tab, Ctrl+K) — показуємо останні запити ще до введення тексту.
        QueryBox.GotFocus += (_, _) => _viewModel?.ShowSuggestions();
        SuggestionsList.Tapped += OnSuggestionTapped;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _viewModel = DataContext as SearchViewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested += OnFocusRequested;
            // Сторінку щойно показано (старт застосунку, перехід з меню чи Ctrl+K) — курсор одразу в поле.
            _viewModel.TakeFocusRequest();
            FocusQueryBox();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnFocusRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.TakeFocusRequest() == true)
        {
            FocusQueryBox();
        }
    }

    /// <summary>Фокусуємо, коли UI-потік звільниться: одразу після перемикання сторінки поле ще не в розмітці.</summary>
    private void FocusQueryBox() =>
        Dispatcher.UIThread.Post(() =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        });

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                viewModel.SubmitCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                viewModel.EscapeCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnSuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel?.SelectedSuggestion is { } suggestion)
        {
            // Спершу фокус: GotFocus відкриває підказки, а пошук одразу після цього їх закриває.
            // У зворотному порядку список підказок лишився б висіти над результатами.
            QueryBox.Focus();
            _viewModel.ApplySuggestionCommand.Execute(suggestion);
        }
    }
}