namespace LocalMind.App.Services;

/// <summary>UI-потік застосунку. Окремий інтерфейс — щоб у тестах керувати, коли виконуються дії з фонових подій.</summary>
public interface IUiDispatcher
{
    /// <summary>Ставить дію в чергу UI-потоку і повертається одразу.</summary>
    void Post(Action action);
}