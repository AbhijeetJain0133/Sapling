namespace Sapling.Shared.Contracts;

/// <summary>What an emailed one-time code is for; a code issued for one purpose cannot be spent on the other.</summary>
public static class CodePurposes
{
    public const string Register = "register";
    public const string ResetPassword = "reset-password";

    public static bool IsKnown(string? purpose) => purpose is Register or ResetPassword;
}

public sealed record SendCodeRequest(string Email);

public sealed record VerifyCodeRequest(string Email, string Code);

/// <summary>Proof that the email was verified, spent by the final step (create account or set a new password).</summary>
public sealed record VerifyCodeResponse(string Ticket);

public sealed record CompleteRegistrationRequest(string Email, string Ticket, string Password, string? FullName);

public sealed record ResetPasswordRequest(string Email, string Ticket, string Password);

/// <summary>Error body for the account endpoints. <see cref="Restart"/> means the verification expired and the flow starts over.</summary>
public sealed record AccountError(string Message, bool Restart = false);
