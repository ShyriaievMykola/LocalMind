using LocalMind.Core.Abstractions;
using LocalMind.Core.Configuration;
using LocalMind.Core.Models;
using Microsoft.Extensions.Options;

namespace LocalMind.Core.Ingestion.Chunking;

/// <summary>Ріже текст на фрагменти за абзацами та заголовками.</summary>
public sealed class ParagraphChunker : IChunker
{
    private readonly ChunkingOptions _options;
    private readonly ITokenCounter _tokenCounter;
    private readonly FixedSizeChunker _fixedSizeChunker;

    public ParagraphChunker(IOptions<ChunkingOptions> options, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenCounter);

        _options = options.Value;
        var validationResult = new ChunkingOptionsValidator().Validate(Options.DefaultName, _options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(ChunkingOptions),
                validationResult.Failures);
        }

        _tokenCounter = tokenCounter;
        _fixedSizeChunker = new FixedSizeChunker(options, tokenCounter);
    }

    /// <inheritdoc />
    public IReadOnlyList<Chunk> Split(Guid documentId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<Chunk>();
        var currentStart = -1;
        var currentEnd = -1;

        foreach (var paragraph in GetParagraphs(text))
        {
            var paragraphText = text[paragraph.Start..paragraph.End];
            if (_tokenCounter.CountTokens(paragraphText) > _options.ChunkSizeTokens)
            {
                AddCurrentChunk();

                foreach (var chunk in _fixedSizeChunker.Split(documentId, paragraphText))
                {
                    chunks.Add(new Chunk(
                        chunk.Id,
                        documentId,
                        chunks.Count,
                        chunk.Text,
                        paragraph.Start + chunk.StartOffset));
                }

                continue;
            }

            if (currentStart >= 0)
            {
                var combinedText = text[currentStart..paragraph.End];
                if (_tokenCounter.CountTokens(combinedText) <= _options.ChunkSizeTokens)
                {
                    currentEnd = paragraph.End;
                    continue;
                }

                AddCurrentChunk();
            }

            currentStart = paragraph.Start;
            currentEnd = paragraph.End;
        }

        AddCurrentChunk();
        return chunks;

        void AddCurrentChunk()
        {
            if (currentStart < 0)
            {
                return;
            }

            chunks.Add(new Chunk(
                Guid.NewGuid(),
                documentId,
                chunks.Count,
                text[currentStart..currentEnd],
                currentStart));
            currentStart = -1;
            currentEnd = -1;
        }
    }

    private static List<TextRange> GetParagraphs(string text)
    {
        var blocks = new List<ParagraphBlock>();
        var paragraphStart = -1;
        var paragraphEnd = -1;

        for (var lineStart = 0; lineStart < text.Length;)
        {
            var lineEnd = lineStart;
            while (lineEnd < text.Length && text[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            if (IsBlank(line))
            {
                AddParagraphBlock();
            }
            else if (IsMarkdownHeading(line))
            {
                AddParagraphBlock();
                blocks.Add(new ParagraphBlock(lineStart, lineEnd, IsHeading: true));
            }
            else if (IsSetextHeading(line) && paragraphStart >= 0)
            {
                blocks.Add(new ParagraphBlock(paragraphStart, lineEnd, IsHeading: true));
                paragraphStart = -1;
                paragraphEnd = -1;
            }
            else
            {
                if (paragraphStart < 0)
                {
                    paragraphStart = lineStart;
                }

                paragraphEnd = lineEnd;
            }

            if (lineEnd < text.Length && text[lineEnd] == '\r')
            {
                lineEnd++;
            }

            if (lineEnd < text.Length && text[lineEnd] == '\n')
            {
                lineEnd++;
            }

            lineStart = lineEnd;
        }

        AddParagraphBlock();

        var paragraphs = new List<TextRange>();
        var pendingHeadingStart = -1;
        foreach (var block in blocks)
        {
            if (block.IsHeading)
            {
                if (pendingHeadingStart < 0)
                {
                    pendingHeadingStart = block.Start;
                }

                continue;
            }

            paragraphs.Add(new TextRange(
                pendingHeadingStart >= 0 ? pendingHeadingStart : block.Start,
                block.End));
            pendingHeadingStart = -1;
        }

        if (pendingHeadingStart >= 0)
        {
            paragraphs.Add(new TextRange(pendingHeadingStart, blocks[^1].End));
        }

        return paragraphs;

        void AddParagraphBlock()
        {
            if (paragraphStart < 0)
            {
                return;
            }

            blocks.Add(new ParagraphBlock(paragraphStart, paragraphEnd, IsHeading: false));
            paragraphStart = -1;
            paragraphEnd = -1;
        }
    }

    private static bool IsBlank(ReadOnlySpan<char> line)
    {
        foreach (var character in line)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMarkdownHeading(ReadOnlySpan<char> line)
    {
        var index = 0;
        while (index < line.Length && line[index] == ' ')
        {
            index++;
        }

        if (index > 3)
        {
            return false;
        }

        var headingStart = index;
        while (index < line.Length && line[index] == '#')
        {
            index++;
        }

        var headingLength = index - headingStart;
        return headingLength is > 0 and <= 6 &&
               (index == line.Length || char.IsWhiteSpace(line[index]));
    }

    private static bool IsSetextHeading(ReadOnlySpan<char> line)
    {
        var index = 0;
        while (index < line.Length && line[index] == ' ')
        {
            index++;
        }

        if (index > 3 || index == line.Length || line[index] is not '=' and not '-')
        {
            return false;
        }

        var marker = line[index];
        while (index < line.Length && line[index] == marker)
        {
            index++;
        }

        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return index == line.Length;
    }

    private readonly record struct ParagraphBlock(int Start, int End, bool IsHeading);

    private readonly record struct TextRange(int Start, int End);
}