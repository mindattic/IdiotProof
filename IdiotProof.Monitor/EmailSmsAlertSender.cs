using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace IdiotProof.Monitor;

/// <summary>
/// Sends a text alert via a carrier email-to-SMS gateway (e.g.
/// <c>5551234567@tmomail.net</c> for T-Mobile) — the same "email method"
/// MindAttic.Psst uses, reading the SAME <c>%APPDATA%\MindAttic\Notifications\
/// providers.json</c> file (surfaced into IConfiguration by the
/// AddMindAtticVaultFiles() chain every IdiotProof host already calls; the
/// "Notifications" bucket is one of MindAttic.Vault's default buckets, so no
/// extra wiring was needed to see it).
///
/// Deliberately NOT a dependency on MindAttic.Psst itself: that assembly
/// targets net10.0-windows + FrameworkReference Microsoft.WindowsDesktop.App
/// (for its audible sound-effect feature), which IdiotProof.Monitor — a
/// background console service with no desktop session — has no business
/// pulling in just to send an email. This is the same MailKit-based
/// connect/send logic, minus the sound and the CLI wrapper.
/// </summary>
public sealed class EmailSmsAlertSender(IConfiguration configuration, ILogger<EmailSmsAlertSender> logger)
{
    private const string Section = "MindAttic:Vault:Notifications";

    /// <summary>
    /// Sends <paramref name="message"/> to the configured toEmail gateway
    /// address(es). Returns false (logged, never throws) when config is
    /// missing or the send fails — a notification failure must never affect
    /// the scan/strategy it's reporting on.
    /// </summary>
    public async Task<bool> TrySendAsync(string message, CancellationToken ct = default)
    {
        var section = configuration.GetSection(Section);
        var emailSection = section.GetSection("email");
        var host = emailSection["smtpHost"];
        var user = emailSection["username"];
        var pass = emailSection["password"];
        var from = emailSection["from"];
        var toEmail = section["toEmail"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogWarning("EmailSmsAlertSender: {Section}:email is not fully configured — alert not sent.", Section);
            return false;
        }
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            logger.LogWarning("EmailSmsAlertSender: {Section}:toEmail is not set — alert not sent.", Section);
            return false;
        }

        var port = int.TryParse(emailSection["smtpPort"], out var p) && p is > 0 and <= 65535 ? p : 587;
        var recipients = toEmail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var smtp = new SmtpClient { CheckCertificateRevocation = false };
        try
        {
            var secure = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await smtp.ConnectAsync(host, port, secure, ct);
            await smtp.AuthenticateAsync(user, pass, ct);

            var fromAddress = MailboxAddress.Parse(from);
            var sentAny = false;
            foreach (var to in recipients)
            {
                try
                {
                    var mail = new MimeMessage();
                    mail.From.Add(fromAddress);
                    mail.To.Add(MailboxAddress.Parse(to));
                    // No Subject — an empty header renders as "/ /" dividers on
                    // some carrier gateways; omitting it entirely is cleaner.
                    mail.Body = new TextPart("plain") { Text = message };
                    await smtp.SendAsync(mail, ct);
                    sentAny = true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "EmailSmsAlertSender: send to {To} failed.", to);
                }
            }
            try { await smtp.DisconnectAsync(true, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "EmailSmsAlertSender: disconnect after send failed (message may still have been delivered)."); }
            return sentAny;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EmailSmsAlertSender: SMTP connect/auth failed — alert not sent.");
            return false;
        }
    }
}
