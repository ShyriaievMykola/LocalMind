using LocalMind.Core.Abstractions;
using LocalMind.Core.Ingestion.Parsers;
using LocalMind.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Core.Tests.Ingestion;

public class ParserRegistryTests
{
    [Fact]
    public void TryGetParser_KnownExtension_ReturnsParser()
    {
        var parser = new TestParser(".md");
        var registry = new ParserRegistry([parser]);

        var found = registry.TryGetParser("document.md", out var result);

        Assert.True(found);
        Assert.Same(parser, result);
    }

    [Fact]
    public void TryGetParser_UnknownExtension_ReturnsFalse()
    {
        var registry = new ParserRegistry([new TestParser(".md")]);

        var found = registry.TryGetParser("document.pdf", out var parser);

        Assert.False(found);
        Assert.Null(parser);
    }

    [Fact]
    public void TryGetParser_ExtensionWithDifferentCase_ReturnsParser()
    {
        var parser = new TestParser(".md");
        var registry = new ParserRegistry([parser]);

        var found = registry.TryGetParser("document.MD", out var result);

        Assert.True(found);
        Assert.Same(parser, result);
    }

    [Fact]
    public void TryGetParser_FileWithoutExtension_ReturnsFalse()
    {
        var registry = new ParserRegistry([new TestParser(".md")]);

        var found = registry.TryGetParser("README", out var parser);

        Assert.False(found);
        Assert.Null(parser);
    }

    [Fact]
    public void Constructor_DuplicateExtension_Throws()
    {
        var parsers = new IDocumentParser[]
        {
            new TestParser(".md"),
            new TestParser(".MD")
        };

        Assert.Throws<InvalidOperationException>(() => new ParserRegistry(parsers));
    }

    [Fact]
    public void AddLocalMindCore_RegistersParsersAndRegistry()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var provider = new ServiceCollection()
            .AddLocalMindCore(configuration)
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<ParserRegistry>();

        Assert.Equal(4, provider.GetServices<IDocumentParser>().Count());
        Assert.True(registry.TryGetParser("document.txt", out _));
        Assert.True(registry.TryGetParser("document.md", out _));
        Assert.True(registry.TryGetParser("document.pdf", out _));
        Assert.True(registry.TryGetParser("document.docx", out _));
    }

    private sealed class TestParser(params string[] extensions) : IDocumentParser
    {
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(extensions);

        public Task<ParsedDocument> ParseAsync(Stream content, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ParsedDocument(string.Empty));
        }
    }
}
