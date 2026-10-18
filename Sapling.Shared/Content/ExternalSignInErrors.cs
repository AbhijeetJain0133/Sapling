namespace Sapling.Shared.Content;

/// <summary>What to tell the student when the server bounces a Google sign-in back with an error code.</summary>
public static class ExternalSignInErrors
{
    public static string Message(string? code) => code switch
    {
        "unverified" => "Google has not verified that email address yet, so it cannot sign you in. Verify it with Google, or use your email and password.",
        "no-email" => "Google did not share an email address. Allow email access when Google asks, or use your email and password.",
        "locked" => "This account is locked for a few minutes after too many attempts. Try again shortly.",
        "unavailable" => "Google sign-in is not set up on this server yet. Use your email and password.",
        "institute" => "That account signs in on the Sapling website, not in the app.",
        _ => "Google sign-in did not finish. Try again, or use your email and password.",
    };
}
