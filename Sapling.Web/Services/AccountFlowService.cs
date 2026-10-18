using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Sapling.Shared.Contracts;
using Sapling.Web.Data;

namespace Sapling.Web.Services;

public sealed record AccountFlowResult(bool Succeeded, string? Error = null, string? Ticket = null, bool Restart = false)
{
    public static AccountFlowResult Ok(string? ticket = null) => new(true, Ticket: ticket);

    public static AccountFlowResult Fail(string error, bool restart = false) => new(false, error, Restart: restart);
}

/// <summary>
/// Email one-time codes for sign-up and password reset: send a code, trade a correct code for a short-lived
/// ticket, then spend the ticket to create the account or set the password. Shared by the web pages and the API.
/// </summary>
public sealed class AccountFlowService(
    UserManager<AppUser> users,
    IEmailDelivery email,
    IMemoryCache cache,
    IHttpContextAccessor http)
{
    private const int CodeDigits = 6;
    private const int MaxAttempts = 5;
    private const int MaxSendsPerEmailPerHour = 5;
    private const int MaxSendsPerClientPerHour = 20;

    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);

    private static readonly AccountFlowResult WrongCode = AccountFlowResult.Fail("That code is not right, or it has expired. Check the email or ask for a new code.");
    private static readonly AccountFlowResult Expired = AccountFlowResult.Fail("That verification expired. Start again to get a new code.", restart: true);

    public static string Clean(string? email) => (email ?? "").Replace(" ", "").Trim();

    public async Task<AccountFlowResult> SendRegistrationCodeAsync(string rawEmail, CancellationToken ct = default)
    {
        var address = Clean(rawEmail);
        if (!IsEmail(address))
        {
            return AccountFlowResult.Fail("That does not look like an email address.");
        }

        if (await users.FindByEmailAsync(address) is not null)
        {
            return AccountFlowResult.Fail("An account with that email already exists. Sign in, or reset your password.");
        }

        return await SendCodeAsync(CodePurposes.Register, address, ct);
    }

    public async Task<AccountFlowResult> SendPasswordResetCodeAsync(string rawEmail, CancellationToken ct = default)
    {
        var address = Clean(rawEmail);
        if (!IsEmail(address))
        {
            return AccountFlowResult.Fail("That does not look like an email address.");
        }

        // Answer the same whether or not the account exists, so this form cannot be used to find out who has one.
        return await users.FindByEmailAsync(address) is null
            ? AccountFlowResult.Ok()
            : await SendCodeAsync(CodePurposes.ResetPassword, address, ct);
    }

    public AccountFlowResult VerifyCode(string purpose, string rawEmail, string? rawCode)
    {
        var key = CodeKey(purpose, Clean(rawEmail));
        var code = (rawCode ?? "").Trim();
        if (!cache.TryGetValue(key, out PendingCode? pending) || pending is null)
        {
            return WrongCode;
        }

        if (!FixedEquals(pending.Code, code))
        {
            if (Interlocked.Increment(ref pending.Attempts) >= MaxAttempts)
            {
                cache.Remove(key);
                return AccountFlowResult.Fail("Too many wrong tries. Ask for a new code.");
            }

            return WrongCode;
        }

        cache.Remove(key);
        var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set(TicketKey(purpose, Clean(rawEmail)), ticket, TicketLifetime);
        return AccountFlowResult.Ok(ticket);
    }

    public async Task<(AppUser? User, AccountFlowResult Result)> CompleteRegistrationAsync(
        string rawEmail, string? ticket, string password, string? fullName, string accountType = AccountTypes.Student)
    {
        var address = Clean(rawEmail);
        if (!HasTicket(CodePurposes.Register, address, ticket))
        {
            return (null, Expired);
        }

        var user = new AppUser
        {
            UserName = address,
            Email = address,
            EmailConfirmed = true,
            FullName = (fullName ?? "").Trim(),
            AccountType = accountType,
        };

        // A weak password leaves the ticket in place so the student can simply try another one.
        var created = await users.CreateAsync(user, password ?? "");
        if (!created.Succeeded)
        {
            return (null, AccountFlowResult.Fail(Describe(created)));
        }

        cache.Remove(TicketKey(CodePurposes.Register, address));
        return (user, AccountFlowResult.Ok());
    }

    public async Task<(AppUser? User, AccountFlowResult Result)> ResetPasswordAsync(string rawEmail, string? ticket, string password)
    {
        var address = Clean(rawEmail);
        var user = await users.FindByEmailAsync(address);
        if (user is null || !HasTicket(CodePurposes.ResetPassword, address, ticket))
        {
            return (null, Expired);
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password ?? "");
        if (!reset.Succeeded)
        {
            return (null, AccountFlowResult.Fail(Describe(reset)));
        }

        cache.Remove(TicketKey(CodePurposes.ResetPassword, address));

        // A deactivated account keeps its block: clearing the lockout below would otherwise undo an admin's decision.
        if (AccountStatus.IsDeactivated(user))
        {
            return (null, AccountFlowResult.Fail("This account has been deactivated. Contact your administrator."));
        }

        // Proving control of the inbox is enough to clear a lockout from earlier wrong passwords.
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);

        // Confirmed now, even for accounts that registered before email verification existed.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        return (user, AccountFlowResult.Ok());
    }

    private async Task<AccountFlowResult> SendCodeAsync(string purpose, string address, CancellationToken ct)
    {
        var key = CodeKey(purpose, address);
        if (cache.TryGetValue(key, out PendingCode? existing) && existing is not null)
        {
            var wait = existing.SentAt + ResendCooldown - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return AccountFlowResult.Fail($"A code was just sent. Wait {Math.Ceiling(wait.TotalSeconds)} seconds before asking for another.");
            }
        }

        if (!TakeSendAllowance($"sends:email:{address.ToUpperInvariant()}", MaxSendsPerEmailPerHour)
            || !TakeSendAllowance($"sends:client:{ClientAddress()}", MaxSendsPerClientPerHour))
        {
            return AccountFlowResult.Fail("Too many codes were requested. Try again in an hour.");
        }

        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, CodeDigits)).ToString($"D{CodeDigits}");
        var sent = await email.SendAsync(Compose(purpose, address, code), ct);
        if (!sent)
        {
            return AccountFlowResult.Fail("We could not send the email just now. Try again in a minute.");
        }

        // A new code replaces the old one, so only the most recent email works.
        cache.Set(key, new PendingCode(code, DateTimeOffset.UtcNow), CodeLifetime);
        return AccountFlowResult.Ok();
    }

    private bool HasTicket(string purpose, string address, string? ticket) =>
        !string.IsNullOrEmpty(ticket)
        && cache.TryGetValue(TicketKey(purpose, address), out string? expected)
        && expected is not null
        && FixedEquals(expected, ticket);

    private bool TakeSendAllowance(string key, int limit)
    {
        var counter = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SendWindow;
            return new SendCounter();
        })!;

        return Interlocked.Increment(ref counter.Count) <= limit;
    }

    private string ClientAddress() => http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();

    private static OutgoingEmail Compose(string purpose, string address, string code)
    {
        var (subject, lead) = purpose == CodePurposes.Register
            ? ($"{code} is your Sapling verification code", "Use this code to finish creating your Sapling account.")
            : ($"{code} is your Sapling password reset code", "Use this code to set a new password for your Sapling account.");

        var minutes = (int)CodeLifetime.TotalMinutes;
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:0 auto;padding:24px;color:#1f2328">
              <p style="font-size:18px;font-weight:600;margin:0 0 16px">Sapling</p>
              <p style="margin:0 0 16px">{WebUtility.HtmlEncode(lead)}</p>
              <p style="font-size:32px;font-weight:700;letter-spacing:8px;margin:0 0 16px;font-family:Consolas,monospace">{code}</p>
              <p style="margin:0 0 8px;color:#57606a">It expires in {minutes} minutes. Do not share it with anyone.</p>
              <p style="margin:0;color:#57606a">If you did not ask for this, you can ignore this email.</p>
            </div>
            """;

        var text = $"{lead}\n\n{code}\n\nIt expires in {minutes} minutes. Do not share it with anyone.\nIf you did not ask for this, you can ignore this email.";
        return new OutgoingEmail(address, subject, html, text);
    }

    private static string Describe(IdentityResult result) =>
        result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail")
            ? "An account with that email already exists. Sign in, or reset your password."
            : string.Join(" ", result.Errors.Select(e => e.Description).Distinct());

    private static bool IsEmail(string address) => address.Length <= 256 && new EmailAddressAttribute().IsValid(address);

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string CodeKey(string purpose, string address) => $"otp:{purpose}:{address.ToUpperInvariant()}";

    private static string TicketKey(string purpose, string address) => $"otp-ticket:{purpose}:{address.ToUpperInvariant()}";

    private sealed class PendingCode(string code, DateTimeOffset sentAt)
    {
        public readonly string Code = code;
        public readonly DateTimeOffset SentAt = sentAt;
        public int Attempts;
    }

    private sealed class SendCounter
    {
        public int Count;
    }
}
