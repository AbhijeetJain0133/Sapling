using Azure;
using Azure.Communication.Email;

namespace Sapling.Web.Services;

public sealed record OutgoingEmail(string To, string Subject, string Html, string PlainText);

public interface IEmailDelivery
{
    /// <summary>Returns false when the message could not be handed to the provider.</summary>
    Task<bool> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}

public sealed class AzureEmailDelivery(EmailClient client, string senderAddress, ILogger<AzureEmailDelivery> logger) : IEmailDelivery
{
    public async Task<bool> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var message = new EmailMessage(
            senderAddress,
            email.To,
            new EmailContent(email.Subject) { Html = email.Html, PlainText = email.PlainText });

        try
        {
            // Started, not Completed: Azure has accepted it, and polling for delivery would keep the student waiting.
            await client.SendAsync(WaitUntil.Started, message, ct);
            return true;
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Azure Communication Services rejected an email ({Status} {Code})", ex.Status, ex.ErrorCode);
            return false;
        }
    }
}

/// <summary>Used when Azure email is not configured in development, so sign-up can be tested without credentials.</summary>
public sealed class LoggedEmailDelivery(ILogger<LoggedEmailDelivery> logger) : IEmailDelivery
{
    public Task<bool> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        logger.LogWarning("Azure email is not configured. Would have sent to {To}: {Subject}", email.To, email.Subject);
        return Task.FromResult(true);
    }
}

/// <summary>Outside development a missing configuration must fail loudly rather than write codes to the logs.</summary>
public sealed class UnconfiguredEmailDelivery(ILogger<UnconfiguredEmailDelivery> logger) : IEmailDelivery
{
    public Task<bool> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        logger.LogError("Cannot send email: set Email:Azure:ConnectionString and Email:Azure:SenderAddress.");
        return Task.FromResult(false);
    }
}
