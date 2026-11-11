using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using LocalMind.Core.Models;

namespace LocalMind.App.Controls;

/// <summary>Показує уривок із підсвіткою діапазонів, наданих ядром.</summary>
public sealed class HighlightedSnippet : TextBlock
{
    public static readonly StyledProperty<Snippet?> SnippetProperty =
        AvaloniaProperty.Register<HighlightedSnippet, Snippet?>(nameof(Snippet));

    public Snippet? Snippet
    {
        get => GetValue(SnippetProperty);
        set => SetValue(SnippetProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SnippetProperty)
        {
            return;
        }

        var inlines = new InlineCollection();
        foreach (var segment in SnippetSegments.Create(Snippet))
        {
            var run = new Run(segment.Text);
            if (segment.IsHighlighted)
            {
                // Кольори Run.highlight задає спільна тема (#117).
                // До її підключення виділення читабельне завдяки накресленню.
                run.Classes.Add("highlight");
                run.Bind(TextElement.BackgroundProperty, new DynamicResourceExtension("Lm.Brush.HighlightBackground"));
                run.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("Lm.Brush.HighlightForeground"));
                run.FontWeight = FontWeight.SemiBold;
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            }

            inlines.Add(run);
        }

        Inlines = inlines;
    }
}
