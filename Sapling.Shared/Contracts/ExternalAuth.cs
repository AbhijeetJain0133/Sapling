namespace Sapling.Shared.Contracts;

/// <summary>The address the server sends the app back to after Google sign-in. Android registers it as an intent filter.</summary>
public static class ExternalAuth
{
    public const string AppScheme = "sapling";
    public const string AppHost = "auth";
    public const string AppCallback = AppScheme + "://" + AppHost;
}
