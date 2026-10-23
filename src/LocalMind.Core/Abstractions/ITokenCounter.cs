namespace LocalMind.Core.Abstractions;

/// <summary>
/// Рахує токени так, як їх бачить embedding-модель. Потрібен для нарізки фрагментів
/// заданого розміру, щоб фрагмент не перевищував ліміт моделі.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Кількість токенів у тексті; 0 для порожнього тексту.</summary>
    int CountTokens(string text);
}
