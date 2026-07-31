namespace JiraLite.Api.Common.Infrastructure.Email;

/// <summary>
/// Outbound email abstraction so the delivery mechanism (SMTP today) is a configuration/DI
/// concern, not something any feature handler talks to directly. spec/20-coding-guidelines.md §7.
/// Always invoked from a Hangfire job (spec/13-notifications.md NFR-01), never inline in a request.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}
