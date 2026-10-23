using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>Перетворює тексти на вектори (embeddings) локальною моделлю.</summary>
public interface IEmbedder
{
    /// <summary>
    /// Унікальна назва моделі (наприклад, <c>multilingual-e5-small</c>). Вектори різних моделей несумісні,
    /// тому сховище використовує цю назву, щоб не змішувати їх між собою.
    /// </summary>
    string ModelName { get; }

    /// <summary>Розмірність векторів моделі.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Максимальна кількість токенів одного тексту, яку модель обробляє. Нарізка має давати фрагменти
    /// не довші за цей ліміт (див. <see cref="ITokenCounter"/>).
    /// </summary>
    int MaxTokens { get; }

    /// <summary>Обчислює embeddings для набору текстів.</summary>
    /// <param name="texts">
    /// Тексти будь-якої кількості. Реалізація сама ділить їх на внутрішні батчі розміру,
    /// заданого в налаштуваннях моделі, — викликачу не треба обмежувати розмір набору.
    /// Текст, довший за <see cref="MaxTokens"/>, обрізається до ліміту без винятку
    /// (так поводиться сама модель), тому довгі тексти треба нарізати заздалегідь.
    /// </param>
    /// <param name="purpose">Запит чи фрагмент документа — впливає на префікси деяких моделей.</param>
    /// <param name="cancellationToken">Скасування операції; перевіряється між батчами.</param>
    /// <returns>
    /// По одному вектору на кожен текст, у тому самому порядку. Кожен вектор має довжину
    /// <see cref="Dimensions"/> і L2-нормалізований, тому косинусна схожість дорівнює скалярному добутку.
    /// Для порожнього набору — порожній список.
    /// </returns>
    /// <exception cref="ArgumentException">Серед текстів є <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Операцію скасовано.</exception>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken cancellationToken = default);
}
