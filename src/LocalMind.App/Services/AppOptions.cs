namespace LocalMind.App.Services;

/// <summary>Налаштування самого застосунку. Секція конфігурації — <see cref="SectionName"/>.</summary>
public sealed class AppOptions
{
    public const string SectionName = "LocalMind:App";

    /// <summary>Тема інтерфейсу.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;
}