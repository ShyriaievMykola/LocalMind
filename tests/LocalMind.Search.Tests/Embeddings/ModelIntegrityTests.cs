using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LocalMind.Core.Errors;
using LocalMind.Search.Configuration;
using LocalMind.Search.Embeddings;
using Microsoft.Extensions.Options;

namespace LocalMind.Search.Tests.Embeddings;

public sealed partial class ModelIntegrityTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("localmind-integrity-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void OpenVerified_MatchingHash_ReturnsStreamAtStart()
    {
        var path = Write("model.onnx", "вміст моделі");

        // Регістр hex не важливий.
        using var stream = ModelIntegrity.OpenVerified(path, HashOf(path).ToUpperInvariant(), "Файл моделі");

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void OpenVerified_OtherHash_ThrowsAndExplainsWhatToDo()
    {
        var path = Write("model.onnx", "підмінена модель");
        var expected = new string('a', 64);

        var exception = Assert.Throws<LocalMindException>(() => ModelIntegrity.OpenVerified(path, expected, "Файл моделі"));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains(HashOf(path), exception.Message, StringComparison.Ordinal);
        Assert.Contains("download-models", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenVerified_OtherHash_ReleasesTheFile()
    {
        var path = Write("model.onnx", "підмінена модель");

        Assert.Throws<LocalMindException>(() => ModelIntegrity.OpenVerified(path, new string('a', 64), "Файл моделі"));

        File.Delete(path); // не кине, якщо потік закрито
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void OpenVerified_NoExpectedHash_DoesNotCheck(string? expected)
    {
        var path = Write("model.onnx", "будь-що");

        using var stream = ModelIntegrity.OpenVerified(path, expected, "Файл моделі");

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void OpenVerified_WhileOpen_FileCannotBeOverwrittenOnWindows()
    {
        // На Linux і macOS блокування дорадчі, тож перевіряємо там, де вони обов'язкові.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Write("model.onnx", "вміст");
        using var stream = ModelIntegrity.OpenVerified(path, HashOf(path), "Файл моделі");

        Assert.Throws<IOException>(() => File.WriteAllText(path, "підміна"));
    }

    [Fact]
    public void Tokenizer_FileWithOtherHash_IsNotLoaded()
    {
        var options = Options.Create(new EmbeddingModelOptions
        {
            TokenizerPath = Write("tokenizer.model", "не токенізатор"),
            TokenizerSha256 = new string('b', 64),
        });
        var tokenizer = new ModelTokenizer(options);

        var exception = Assert.Throws<LocalMindException>(() => tokenizer.CountTokens("текст"));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Contains("токенізатора", exception.Message, StringComparison.Ordinal);
    }

    [ModelFact]
    public async Task Embedder_ModelWithOtherHash_IsNotLoaded()
    {
        var real = TestModel.Options();
        var options = Options.Create(new EmbeddingModelOptions
        {
            ModelName = real.ModelName,
            ModelPath = Write("model.onnx", "підміна замість моделі"),
            TokenizerPath = real.TokenizerPath,
            ModelSha256 = ConfiguredHash("ModelSha256"),
            Dimensions = real.Dimensions,
        });
        using var embedder = new OnnxEmbedder(options, new ModelTokenizer(options));

        var exception = await Assert.ThrowsAsync<LocalMindException>(
            () => embedder.EmbedAsync(["текст"], Core.Models.EmbeddingPurpose.Query));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Contains("моделі", exception.Message, StringComparison.Ordinal);
    }

    [ModelFact]
    public void RealModelFiles_MatchConfiguredHashes()
    {
        var real = TestModel.Options();

        using var model = ModelIntegrity.OpenVerified(real.ModelPath, ConfiguredHash("ModelSha256"), "Файл моделі");
        using var tokenizer = ModelIntegrity.OpenVerified(real.TokenizerPath, ConfiguredHash("TokenizerSha256"), "Файл токенізатора");
    }

    [Theory]
    [InlineData("App")]
    [InlineData("Cli")]
    public void AppSettings_HashesMatchDownloadScripts(string host)
    {
        var settings = File.ReadAllText(RepositoryFile("src", $"LocalMind.{host}", "appsettings.json"));
        var scripts = File.ReadAllText(RepositoryFile("scripts", "download-models.ps1"))
            + File.ReadAllText(RepositoryFile("scripts", "download-models.sh"));

        foreach (var key in (string[])["ModelSha256", "TokenizerSha256"])
        {
            var hash = JsonHash(settings, key);
            Assert.Matches("^[0-9a-f]{64}$", hash);
            Assert.Contains(hash, scripts, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665", true)]
    [InlineData("CA456C06B3A9505DDFD9131408916DD79290368331E7D76BB621F1CBA6BC8665", true)]
    [InlineData("ca456c06", false)]
    [InlineData("zz456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665", false)]
    public void Validator_ChecksHashFormat(string hash, bool valid)
    {
        var options = new EmbeddingModelOptions
        {
            ModelName = "m",
            ModelPath = "m.onnx",
            TokenizerPath = "t.model",
            Dimensions = 4,
            ModelSha256 = hash,
            TokenizerSha256 = hash,
        };

        var result = new EmbeddingModelOptionsValidator().Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.Contains(result.Failures!, f => f.Contains("SHA-256", StringComparison.Ordinal));
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string HashOf(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Хеш із appsettings.json застосунку — єдине місце правди для тестів із справжньою моделлю.</summary>
    private static string ConfiguredHash(string key) =>
        JsonHash(File.ReadAllText(RepositoryFile("src", "LocalMind.App", "appsettings.json")), key);

    private static string JsonHash(string json, string key)
    {
        var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
        Assert.True(match.Success, $"У appsettings.json немає {key}.");
        return match.Groups[1].Value;
    }

    private static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalMind.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
        }

        throw new DirectoryNotFoundException("Корінь репозиторію (LocalMind.slnx) не знайдено над тестовою текою.");
    }
}
