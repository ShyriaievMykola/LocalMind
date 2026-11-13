namespace LocalMind.Core.Errors;

/// <summary>
/// Файлів моделі embeddings (або токенізатора) немає на диску. На відміну від пошкодженого чи підміненого файла,
/// це не загроза, а відсутня необов'язкова частина: індексація й keyword-пошук можуть працювати без моделі.
/// </summary>
public sealed class ModelNotFoundException : LocalMindException
{
    public ModelNotFoundException()
    {
    }

    public ModelNotFoundException(string message)
        : base(message)
    {
    }

    public ModelNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
