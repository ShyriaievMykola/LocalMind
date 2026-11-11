namespace LocalMind.Search.Embeddings;

/// <summary>
/// Ліниве завантаження, яке запам'ятовує лише успіх. На відміну від <see cref="Lazy{T}"/>, виняток не кешується:
/// якщо файл моделі докачали, поки програма працює, наступний виклик завантажить його без перезапуску.
/// Фабрика виконується під замком, тому важкий об'єкт (сесія ONNX) не створюється двічі.
/// </summary>
internal sealed class LoadOnce<T>(Func<T> factory)
    where T : class
{
    private readonly Lock _lock = new();
    private volatile T? _value;

    /// <summary>Чи вже завантажено.</summary>
    public bool IsValueCreated => _value is not null;

    /// <summary>Значення; за потреби завантажує його.</summary>
    public T Value
    {
        get
        {
            var value = _value;
            if (value is not null)
            {
                return value;
            }

            lock (_lock)
            {
                return _value ??= factory();
            }
        }
    }
}
