using Erp.Modules.Platform.Application;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Erp.Modules.Platform.Infrastructure;

/// <summary>Sends mail through the configured SMTP server. Each message opens its own short connection: volumes are low and it avoids stale ones.</summary>
public sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        MimeMessage message;
        try
        {
            message = new MimeMessage();
            // Host and addresses are validated at start-up, before email can be enabled.
            message.From.Add(new MailboxAddress(options.FromName, options.FromAddress!));
            message.To.Add(new MailboxAddress(email.ToName ?? string.Empty, email.ToAddress));
            message.Subject = email.Subject;
            // The same id on every attempt at this message, in the sender's own domain (RFC 5322 requires an @).
            if (!string.IsNullOrWhiteSpace(email.MessageId))
                message.MessageId = $"{email.MessageId}@{options.FromAddress!.Split('@')[^1]}";
            message.Body = new BodyBuilder { TextBody = email.Text, HtmlBody = email.Html }.ToMessageBody();
        }
        catch (Exception exception) when (exception is ParseException or ArgumentException or FormatException)
        {
            throw new EmailDeliveryException($"The message could not be built: {exception.Message}", permanent: true, exception);
        }

        try
        {
            using var client = new SmtpClient { Timeout = 30_000 };
            await client.ConnectAsync(options.Host!, options.Port, options.Security switch
            {
                EmailSecurity.None => SecureSocketOptions.None,
                EmailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls
            }, cancellationToken);
            if (!string.IsNullOrEmpty(options.Username)) await client.AuthenticateAsync(options.Username, options.Password ?? string.Empty, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SmtpCommandException exception) when ((int)exception.StatusCode is >= 500 and < 600 && exception.ErrorCode is SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.MessageNotAccepted)
        {
            // The server refused this message or address outright; trying again will not change that.
            throw new EmailDeliveryException($"Rejected by the mail server: {exception.Message}", permanent: true, exception);
        }
        catch (Exception exception)
        {
            // Connection refused, timeouts, temporary (4xx) refusals, authentication trouble: worth another try later.
            throw new EmailDeliveryException(exception.Message, permanent: false, exception);
        }
    }
}
