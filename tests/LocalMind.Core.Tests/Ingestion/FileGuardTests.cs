using System.IO.Compression;
using LocalMind.Core.Configuration;
using LocalMind.Core.Errors;
using LocalMind.Core.Ingestion;

namespace LocalMind.Core.Tests.Ingestion;

public class FileGuardTests
{
    private static readonly IngestionOptions Limits = new()
    {
        MaxFileSizeBytes = 1024,
        MaxArchiveEntries = 10,
        MaxArchiveUncompressedBytes = 1024 * 1024,
    };

    [Fact]
    public void CheckSize_AtLimit_Passes()
    {
        FileGuard.CheckSize(1024, Limits);
    }

    [Fact]
    public void CheckSize_AboveLimit_ThrowsWithSizes()
    {
        var exception = Assert.Throws<DocumentParseException>(() => FileGuard.CheckSize(60L * 1024 * 1024, new IngestionOptions()));

        Assert.Contains("завеликий", exception.Message, StringComparison.Ordinal);
        Assert.Contains("60 МБ", exception.Message, StringComparison.Ordinal);
        Assert.Contains("50 МБ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckContent_TextFileNamedPdf_IsRejected()
    {
        using var stream = Text("Це звичайний текст, а не PDF.");

        var exception = Assert.Throws<DocumentParseException>(() => FileGuard.CheckContent(".pdf", stream, Limits));

        Assert.Contains("%PDF", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("%PDF-1.7\n...")]
    [InlineData("\u0001\u0002 сміття перед заголовком %PDF-1.4")]
    public void CheckContent_PdfSignature_IsAccepted(string content)
    {
        using var stream = Text(content);

        FileGuard.CheckContent(".PDF", stream, Limits);

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CheckContent_PdfSignatureBeyondFirstKilobyte_IsRejected()
    {
        using var stream = Text(new string('x', 2000) + "%PDF-1.4");

        Assert.Throws<DocumentParseException>(() => FileGuard.CheckContent(".pdf", stream, Limits));
    }

    [Fact]
    public void CheckContent_TextNamedDocx_IsRejected()
    {
        using var stream = Text("це не zip");

        var exception = Assert.Throws<DocumentParseException>(() => FileGuard.CheckContent(".docx", stream, Limits));

        Assert.Contains("не zip", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckContent_ZipSignatureButBrokenArchive_IsRejected()
    {
        using var stream = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<DocumentParseException>(() => FileGuard.CheckContent(".docx", stream, Limits));
    }

    [Fact]
    public void CheckContent_RealDocx_IsAccepted()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "sample.docx"));

        FileGuard.CheckContent(".docx", stream, new IngestionOptions());

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CheckContent_ZipBomb_IsRejectedByActualSize()
    {
        // Десять мегабайтів нулів стискаються до кількох кілобайтів: файл малий, а розпакований — завеликий.
        using var stream = Zip(("word/document.xml", new byte[10 * 1024 * 1024]));
        Assert.True(stream.Length < Limits.MaxFileSizeBytes * 100);

        var exception = Assert.Throws<DocumentParseException>(() => FileGuard.CheckContent(".docx", stream, Limits));

        Assert.Contains("zip-бомб", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckContent_ForgedSmallDeclaredSize_NothingBeyondDeclaredIsEverRead()
    {
        using var stream = Zip(("word/document.xml", new byte[10 * 1024 * 1024]));
        ForgeUncompressedSize(stream, forged: 100);

        // Або захист відкидає архів, або (так робить .NET) читання з підробленого запису обрізається заявленим
        // розміром — тоді бомба не розгортається і для парсера. Третього — «прочитали 10 МБ» — бути не може.
        try
        {
            FileGuard.CheckContent(".docx", stream, Limits);
        }
        catch (DocumentParseException)
        {
            return;
        }

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var entry = archive.Entries[0].Open();
        using var sink = new MemoryStream();
        try
        {
            entry.CopyTo(sink);
        }
        catch (InvalidDataException)
        {
        }

        Assert.True(sink.Length <= 100, $"Прочитано {sink.Length} байт при заявлених 100.");
    }

    [Fact]
    public void CheckContent_TooManyEntries_IsRejected()
    {
        using var stream = Zip(Enumerable.Range(0, 11).Select(i => ($"part{i}.xml", new byte[1])).ToArray());

        var exception = Assert.Throws<DocumentParseException>(() => FileGuard.CheckContent(".docx", stream, Limits));

        Assert.Contains("забагато записів", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    public void CheckContent_TextFormats_HaveNoSignature(string extension)
    {
        using var stream = Text("будь-що");

        FileGuard.CheckContent(extension, stream, Limits);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    public void CheckContent_EmptyFile_IsLeftToParsers(string extension)
    {
        using var stream = new MemoryStream();

        FileGuard.CheckContent(extension, stream, Limits);
    }

    private static MemoryStream Text(string content) => new(System.Text.Encoding.UTF8.GetBytes(content));

    private static MemoryStream Zip(params (string Name, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>Підробляє заявлений розмір у локальному заголовку (зсув 22) і в центральному каталозі (зсув 24).</summary>
    private static void ForgeUncompressedSize(MemoryStream stream, int forged)
    {
        var bytes = stream.GetBuffer().AsSpan(0, (int)stream.Length);
        var local = bytes.IndexOf((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]);
        var central = bytes.IndexOf((ReadOnlySpan<byte>)[0x50, 0x4B, 0x01, 0x02]);
        BitConverter.GetBytes(forged).CopyTo(bytes[(local + 22)..]);
        BitConverter.GetBytes(forged).CopyTo(bytes[(central + 24)..]);
        stream.Position = 0;
    }
}
