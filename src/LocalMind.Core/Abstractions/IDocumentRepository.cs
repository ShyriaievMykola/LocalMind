using LocalMind.Core.Models;

namespace LocalMind.Core.Abstractions;

/// <summary>Локальне сховище бібліотеки: відстежувані папки, документи, фрагменти та їхні вектори.</summary>
public interface IDocumentRepository
{
    /// <summary>Відстежувані папки.</summary>
    Task<IReadOnlyList<string>> GetSourceFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Додає папку до відстежуваних; повторне додавання тієї самої папки нічого не змінює.</summary>
    Task AddSourceFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Прибирає папку з відстежуваних разом з усіма її документами — в одній транзакції.</summary>
    /// <returns>Ідентифікатори видалених фрагментів — щоб прибрати їх із <see cref="IVectorIndex"/>.</returns>
    Task<IReadOnlyList<Guid>> RemoveSourceFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Усі документи бібліотеки.</summary>
    Task<IReadOnlyList<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Документ за ідентифікатором або <see langword="null"/>.</summary>
    Task<Document?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Документ за шляхом до файла або <see langword="null"/>.</summary>
    Task<Document?> GetDocumentByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Фрагменти за ідентифікаторами; відсутні ідентифікатори пропускаються.</summary>
    Task<IReadOnlyList<Chunk>> GetChunksAsync(IReadOnlyCollection<Guid> chunkIds, CancellationToken cancellationToken = default);

    /// <summary>Усі збережені вектори поточної моделі — для заповнення <see cref="IVectorIndex"/> під час запуску.</summary>
    IAsyncEnumerable<ChunkVector> GetVectorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Зберігає документ з фрагментами і векторами <b>в одній транзакції</b>. Якщо документ із тим самим
    /// шляхом уже є, його старі фрагменти й вектори повністю замінюються.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="chunks">Фрагменти документа; порожній список для документів без тексту або з помилкою.</param>
    /// <param name="vectors">Вектори фрагментів у тому самому порядку, що й <paramref name="chunks"/>.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Ідентифікатори замінених (видалених) фрагментів — щоб прибрати їх із <see cref="IVectorIndex"/>.</returns>
    /// <exception cref="ArgumentException">Кількість векторів не збігається з кількістю фрагментів.</exception>
    Task<IReadOnlyList<Guid>> SaveAsync(
        Document document,
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken cancellationToken = default);

    /// <summary>Видаляє документ разом із фрагментами і векторами.</summary>
    /// <returns>Ідентифікатори видалених фрагментів; порожній список, якщо документа не було.</returns>
    Task<IReadOnlyList<Guid>> DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);
}
