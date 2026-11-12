using LocalMind.Core.Models;

namespace LocalMind.App.Controls;

internal readonly record struct SnippetSegment(string Text, bool IsHighlighted = false);

/// <summary>Розділяє готовий уривок для малювання; не шукає слів і не перераховує діапазони.</summary>
internal static class SnippetSegments
{
    public static IReadOnlyList<SnippetSegment> Create(Snippet? snippet)
    {
        if (snippet is null)
        {
            return [];
        }

        var segments = new List<SnippetSegment>();
        if (snippet.HasTextBefore)
        {
            segments.Add(new("…"));
        }

        var position = 0;
        foreach (var range in snippet.Highlights)
        {
            if (range.Start > position)
            {
                segments.Add(new(snippet.Text[position..range.Start]));
            }

            segments.Add(new(snippet.Text[range.Start..range.End], IsHighlighted: true));
            position = range.End;
        }

        if (position < snippet.Text.Length)
        {
            segments.Add(new(snippet.Text[position..]));
        }

        if (snippet.HasTextAfter)
        {
            segments.Add(new("…"));
        }

        return segments;
    }
}
