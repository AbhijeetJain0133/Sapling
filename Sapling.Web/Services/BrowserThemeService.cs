using Microsoft.JSInterop;
using Sapling.Shared.Theming;

namespace Sapling.Web.Services;

public sealed class BrowserThemeService(IJSRuntime js) : IThemeService
{
    public ThemeMode Current { get; private set; } = ThemeMode.System;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("sapling.theme.read");
            Current = Parse(stored);
            Changed?.Invoke();
        }
        catch (JSException)
        {
            // Prerendering: the browser is not reachable yet.
        }
    }

    public async Task SetAsync(ThemeMode mode)
    {
        Current = mode;
        await js.InvokeVoidAsync("sapling.theme.write", mode.ToString().ToLowerInvariant());
        Changed?.Invoke();
    }

    private static ThemeMode Parse(string? value) => value switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        _ => ThemeMode.System,
    };
}
