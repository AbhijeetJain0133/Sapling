using Microsoft.JSInterop;
using Sapling.Shared.Theming;

namespace Sapling.Services;

/// <summary>Persists to MAUI preferences and pushes the attribute into the hosted WebView.</summary>
public sealed class MauiThemeService(IJSRuntime js) : IThemeService
{
    private const string Key = "sapling-theme";

    public ThemeMode Current { get; private set; } = Read();

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        Current = Read();
        await ApplyAsync();
        Changed?.Invoke();
    }

    public async Task SetAsync(ThemeMode mode)
    {
        Current = mode;
        Preferences.Default.Set(Key, mode.ToString().ToLowerInvariant());
        Application.Current!.UserAppTheme = mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

        await ApplyAsync();
        Changed?.Invoke();
    }

    private async Task ApplyAsync()
    {
        try
        {
            await js.InvokeVoidAsync("sapling.theme.write", Current.ToString().ToLowerInvariant());
        }
        catch (JSException)
        {
            // WebView not ready yet; the inline bootstrap script covers first paint.
        }
    }

    private static ThemeMode Read() => Preferences.Default.Get(Key, "system") switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        _ => ThemeMode.System,
    };
}
