namespace LocalMind.App.Services;

/// <summary>Діалог вибору папки. Окремий інтерфейс — щоб ViewModel можна було тестувати без вікна.</summary>
public interface IFolderPicker
{
    /// <summary>Показує системний діалог вибору папки.</summary>
    /// <returns>Локальний шлях до обраної папки або <see langword="null"/>, якщо користувач скасував вибір.</returns>
    Task<string?> PickFolderAsync();
}