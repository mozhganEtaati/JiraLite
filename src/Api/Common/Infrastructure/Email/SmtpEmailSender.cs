using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Common.Infrastructure.Email;

/// <summary>V1 email delivery: plain SMTP via the .NET BCL client — no extra dependency needed. spec/13-notifications.md, spec/20-coding-guidelines.md §7.</summary>
public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.EnableSsl };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        using var message = new MailMessage(new MailAddress(settings.FromAddress, settings.FromName), new MailAddress(toEmail))
        {
            Subject = subject,
            Body = body
        };

        await client.SendMailAsync(message, cancellationToken);
    }
}
