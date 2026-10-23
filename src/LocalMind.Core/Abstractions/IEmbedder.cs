using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>Перетворює тексти на вектори (embeddings) локальною моделлю.</summary>
public interface IEmbedder
{
    /// <summary>Назва моделі — зберігається разом із векторами, щоб не змішувати вектори різних моделей.</summary>
    string ModelName { get; }

    /// <summary>Розмірність векторів моделі.</summary>
    int Dimensions { get; }

    /// <summary>Обчислює embeddings для пакета текстів.</summary>
    /// <param name="texts">Тексти; обробляються одним батчем.</param>
    /// <param name="purpose">Запит чи фрагмент документа — впливає на префікси деяких моделей.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>
    /// По одному вектору на кожен текст, у тому самому порядку. Кожен вектор має довжину
    /// <see cref="Dimensions"/> і L2-нормалізований, тому косинусна схожість дорівнює скалярному добутку.
    /// </returns>
    /// <exception cref="OperationCanceledException">Операцію скасовано.</exception>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default);
}
