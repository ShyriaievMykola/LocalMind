using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion.Parsers;

namespace LocalMind.Core.Tests.Ingestion;

public class DocxParserTests
{
    private readonly string _testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public async Task ParseAsync_SampleFixture_ExtractsCyrillicLatinAndTableText()
    {
        await using var content = OpenFixture("sample.docx");

        var result = await new DocxParser().ParseAsync(content);

        Assert.Contains("Заголовок", result.Text);
        Assert.Contains("Абзац", result.Text);
        Assert.Contains("Комірка 1,1", result.Text);
        Assert.Contains("Комірка 2,2", result.Text);
        Assert.Contains("Eng Test", result.Text);
        Assert.Contains('\t', result.Text);
    }

    [Fact]
    public async Task ParseAsync_CorruptedFixture_ThrowsDocumentParseException()
    {
        await using var content = OpenFixture("corrupted.docx");

        var exception = await Assert.ThrowsAsync<DocumentParseException>(
            () => new DocxParser().ParseAsync(content));

        Assert.Contains("DOCX", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task ParseAsync_ExtractsParagraphsHeadingsListsTablesAndTitle()
    {
        using var content = CreateDocument();
        var parser = new DocxParser();

        var result = await parser.ParseAsync(content);

        Assert.Equal("План документа\nЗвичайний абзац\nПункт списку\nПерша клітинка\tДруга клітинка", result.Text);
        Assert.Equal("Звіт за місяць", result.Title);
        Assert.True(content.CanRead);
        Assert.Contains(".docx", parser.SupportedExtensions);
    }

    [Fact]
    public async Task ParseAsync_ReadsNonSeekableStream()
    {
        using var document = CreateDocument();
        using var content = new NonSeekableReadStream(document.ToArray());

        var result = await new DocxParser().ParseAsync(content);

        Assert.Equal("План документа\nЗвичайний абзац\nПункт списку\nПерша клітинка\tДруга клітинка", result.Text);
    }

    [Fact]
    public async Task ParseAsync_NormalizesLineBreaksAndSkipsEmptyTableRows()
    {
        using var content = CreateDocument();
        content.Position = 0;
        using (var document = WordprocessingDocument.Open(content, true))
        {
            var body = document.MainDocumentPart!.Document!.Body!;
            body.Append(
                new Paragraph(new Run(new Text("Рядок 1\r\nРядок 2\rРядок 3"))),
                new Table(
                    new TableRow(
                        new TableCell(new Paragraph()),
                        new TableCell(new Paragraph()))),
                new Paragraph(new Run(new Text("Після таблиці"))));
            document.MainDocumentPart.Document.Save();
        }

        content.Position = 0;
        var result = await new DocxParser().ParseAsync(content);

        Assert.Equal(
            "План документа\nЗвичайний абзац\nПункт списку\nПерша клітинка\tДруга клітинка\nРядок 1\nРядок 2\nРядок 3\nПісля таблиці",
            result.Text);
    }

    [Fact]
    public async Task ParseAsync_ThrowsDocumentParseExceptionForCorruptedDocument()
    {
        using var content = new MemoryStream("це не DOCX"u8.ToArray());
        var parser = new DocxParser();

        var exception = await Assert.ThrowsAsync<DocumentParseException>(
            () => parser.ParseAsync(content));

        Assert.Contains("DOCX", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.True(content.CanRead);
    }

    [Fact]
    public async Task ParseAsync_ReturnsEmptyTextForDocumentWithoutText()
    {
        using var content = CreateDocument();
        content.Position = 0;
        using (var document = WordprocessingDocument.Open(content, true))
        {
            document.MainDocumentPart!.Document!.Body!.RemoveAllChildren();
            document.MainDocumentPart.Document.Save();
        }

        content.Position = 0;
        var result = await new DocxParser().ParseAsync(content);

        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task ParseAsync_ThrowsWhenCancellationWasRequested()
    {
        using var content = CreateDocument();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DocxParser().ParseAsync(content, cancellation.Token));
    }

    private static MemoryStream CreateDocument()
    {
        var content = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   content,
                   WordprocessingDocumentType.Document,
                   true))
        {
            document.PackageProperties.Title = "Звіт за місяць";
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(
                    new MultiLevelType { Val = MultiLevelValues.SingleLevel },
                    new Level(
                        new StartNumberingValue { Val = 1 },
                        new NumberingFormat { Val = NumberFormatValues.Decimal },
                        new LevelText { Val = "%1." })
                    {
                        LevelIndex = 0
                    })
                {
                    AbstractNumberId = 0
                },
                new NumberingInstance(new AbstractNumId { Val = 0 })
                {
                    NumberID = 1
                });
            numberingPart.Numbering.Save();

            var body = mainPart.Document;
            body.Body!.Append(
                new Paragraph(
                    new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                    new Run(new Text("План документа"))),
                new Paragraph(new Run(new Text("Звичайний абзац"))),
                new Paragraph(
                    new ParagraphProperties(
                        new NumberingProperties(
                            new NumberingLevelReference { Val = 0 },
                            new NumberingId { Val = 1 })),
                    new Run(new Text("Пункт списку"))),
                new Table(
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("Перша клітинка")))),
                        new TableCell(new Paragraph(new Run(new Text("Друга клітинка")))))));
            body.Save();
        }

        content.Position = 0;
        return content;
    }

    private FileStream OpenFixture(string fileName)
    {
        return File.OpenRead(Path.Combine(_testDataPath, fileName));
    }

    private sealed class NonSeekableReadStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
