namespace LocalMind.App.ViewModels;

/// <summary>Пункт бокового меню зі сторінкою, отриманою з DI.</summary>
public sealed record NavigationItem(string Title, ViewModelBase Page);
