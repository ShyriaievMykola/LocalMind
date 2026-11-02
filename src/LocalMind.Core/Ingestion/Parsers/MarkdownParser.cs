using System.Text;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LocalMind.Core.Ingestion.Parsers;

public sealed class MarkdownParser : IDocumentParser
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseAdvancedExtensions()
        .Build();

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown"
    };

    public async Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (content.CanSeek && content.Length == 0)
        {
            return new ParsedDocument(string.Empty);
        }

        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var markdownText = await reader.ReadToEndAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(markdownText))
        {
            return new ParsedDocument(string.Empty);
        }

        var document = Markdown.Parse(markdownText, _pipeline);

        string? title = null;
        var builder = new StringBuilder();

        foreach (var block in document)
        {
            if (block is YamlFrontMatterBlock)
            {
                continue;
            }

            if (block is HeadingBlock headingBlock)
            {
                var headingText = ExtractInlineText(headingBlock.Inline).Trim();
                
                if (headingBlock.Level == 1 && title is null && !string.IsNullOrWhiteSpace(headingText))
                {
                    title = headingText;
                }

                EnsureNewLine(builder);
                builder.AppendLine(headingText);
                continue;
            }

            ProcessBlock(block, builder);
        }

        var resultText = builder.ToString().Replace("\r\n", "\n").Trim();

        return new ParsedDocument(resultText, title);
    }

    private static void ProcessBlock(Block block, StringBuilder builder)
    {
        switch (block)
        {
            case YamlFrontMatterBlock:
                break;

            case LeafBlock leafBlock:
                var inlineText = ExtractInlineText(leafBlock.Inline);
                var linesText = leafBlock.Lines.Count > 0 ? leafBlock.Lines.ToString() : string.Empty;

                var combinedText = string.IsNullOrEmpty(inlineText) 
                    ? linesText 
                    : string.IsNullOrEmpty(linesText) ? inlineText : $"{inlineText}\n{linesText}";

                if (!string.IsNullOrWhiteSpace(combinedText))
                {
                    EnsureNewLine(builder);
                    builder.AppendLine(combinedText.TrimEnd());
                }
                break;

            case ContainerBlock containerBlock:
                foreach (var childBlock in containerBlock)
                {
                    ProcessBlock(childBlock, builder);
                }
                break;
        }
    }

    private static string ExtractInlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var current = inline.FirstChild;

        while (current is not null)
        {
            switch (current)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content);
                    break;

                case CodeInline code:
                    sb.Append(code.Content);
                    break;

                case LineBreakInline:
                    sb.Append('\n');
                    break;

                case ContainerInline container:
                    sb.Append(ExtractInlineText(container));
                    break;
            }

            current = current.NextSibling;
        }

        return sb.ToString();
    }

    private static void EnsureNewLine(StringBuilder builder)
    {
        if (builder.Length > 0 && !builder.ToString().EndsWith('\n'))
        {
            builder.AppendLine();
        }
    }
}