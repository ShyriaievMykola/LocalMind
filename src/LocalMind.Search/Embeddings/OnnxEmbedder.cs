using LocalMind.Core.Abstractions;
using LocalMind.Core.Errors;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LocalMind.Search.Embeddings;

/// <summary>Обчислює embeddings ONNX-моделлю локально. Модель завантажується під час першого використання.</summary>
public sealed class OnnxEmbedder : IEmbedder, IDisposable
{
    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string TokenTypeIdsName = "token_type_ids";

    private readonly EmbeddingModelOptions _options;
    private readonly ModelTokenizer _tokenizer;
    private readonly LoadOnce<InferenceSession> _session;
    private readonly Func<byte[], InferenceSession> _sessionFactory;

    public OnnxEmbedder(IOptions<EmbeddingModelOptions> options, ModelTokenizer tokenizer)
        : this(options, tokenizer, model => new InferenceSession(model))
    {
    }

    // sessionFactory створює сесію з перевірених байтів моделі; у тестах підміняється.
    internal OnnxEmbedder(
        IOptions<EmbeddingModelOptions> options,
        ModelTokenizer tokenizer,
        Func<byte[], InferenceSession> sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenizer);

        _options = options.Value;
        _tokenizer = tokenizer;
        _session = new LoadOnce<InferenceSession>(LoadSession);
    }

    /// <inheritdoc />
    public string ModelName => _options.ModelName;

    /// <inheritdoc />
    public int Dimensions => _options.Dimensions;

    /// <inheritdoc />
    public int MaxTokens => _options.MaxTokens;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Any(text => text is null))
        {
            throw new ArgumentException("Серед текстів є null.", nameof(texts));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var prefix = purpose == EmbeddingPurpose.Query ? _options.QueryPrefix : _options.PassagePrefix;
        var encoded = texts.Select(text => _tokenizer.Encode(prefix + text, MaxTokens)).ToArray();

        // Тексти схожої довжини в одному батчі — менше доповнення і швидший прохід моделі.
        var order = Enumerable.Range(0, encoded.Length).OrderBy(i => encoded[i].Length).ToArray();
        var results = new ReadOnlyMemory<float>[encoded.Length];

        foreach (var batch in order.Chunk(_options.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vectors = await Task.Run(() => RunBatch([.. batch.Select(i => encoded[i])]), cancellationToken);
            for (var i = 0; i < batch.Length; i++)
            {
                results[batch[i]] = vectors[i];
            }
        }

        return results;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value.Dispose();
        }
    }

    private float[][] RunBatch(long[][] batch)
    {
        var length = batch.Max(ids => ids.Length);
        var shape = new[] { batch.Length, length };
        var inputIds = new DenseTensor<long>(shape);
        var attentionMask = new DenseTensor<long>(shape);

        for (var row = 0; row < batch.Length; row++)
        {
            for (var column = 0; column < length; column++)
            {
                var isToken = column < batch[row].Length;
                inputIds[row, column] = isToken ? batch[row][column] : ModelTokenizer.PadId;
                attentionMask[row, column] = isToken ? 1 : 0;
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputIdsName, inputIds),
            NamedOnnxValue.CreateFromTensor(AttentionMaskName, attentionMask),
        };

        // Не всі експорти моделей мають цей вхід.
        if (_session.Value.InputMetadata.ContainsKey(TokenTypeIdsName))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(TokenTypeIdsName, new DenseTensor<long>(shape)));
        }

        using var outputs = _session.Value.Run(inputs);
        var hiddenState = outputs[0].AsTensor<float>();
        var outputDimensions = hiddenState.Dimensions[^1];
        if (outputDimensions != Dimensions)
        {
            throw new LocalMindException(
                $"Модель {ModelName} повертає вектори розмірності {outputDimensions}, у налаштуваннях вказано {Dimensions}.");
        }

        var values = hiddenState.ToDenseTensor().Buffer.Span;
        var mask = attentionMask.Buffer.Span;
        var stride = length * Dimensions;
        var vectors = new float[batch.Length][];
        for (var row = 0; row < batch.Length; row++)
        {
            vectors[row] = Pooling.MeanPool(values.Slice(row * stride, stride), mask.Slice(row * length, length), Dimensions);
        }

        return vectors;
    }

    private InferenceSession LoadSession()
    {
        if (!File.Exists(_options.ModelPath))
        {
            throw new LocalMindException(
                $"Файл моделі не знайдено: {_options.ModelPath}. Запустіть scripts/download-models.");
        }

        byte[] model;
        try
        {
            // Сесія створюється з тих самих байтів, що пройшли перевірку хеша, а не з файла за шляхом: між перевіркою
            // і завантаженням файл не підмінити. Байти живуть лише до кінця цього методу, тож двох копій моделі
            // в пам'яті довго немає: після створення сесії масив збирає GC.
            model = ModelIntegrity.ReadVerified(_options.ModelPath, _options.ModelSha256, "Файл моделі");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Найчастіше файл зайнятий: його саме завантажує scripts/download-models.
            throw new LocalMindException(
                $"Файл моделі недоступний: {_options.ModelPath} ({ex.Message}). " +
                "Дочекайтеся завершення завантаження або запустіть scripts/download-models.", ex);
        }

        try
        {
            return _sessionFactory(model);
        }
        catch (OnnxRuntimeException ex)
        {
            // Найчастіше — обірване завантаження: файл є, але неповний.
            throw new LocalMindException(
                $"Файл моделі пошкоджений: {_options.ModelPath}. Запустіть scripts/download-models ще раз.", ex);
        }
    }
}
