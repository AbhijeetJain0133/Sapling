namespace Sapling.Shared.Theming;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    ThemeMode Current { get; }

    event Action? Changed;

    Task InitializeAsync();

    Task SetAsync(ThemeMode mode);
}
