using JiraLite.Api.Common.Infrastructure.Email;

namespace JiraLite.Api.Common.Infrastructure.BackgroundJobs;

/// <summary>
/// spec/13-notifications.md NFR-01/NFR-02 — email is always dispatched asynchronously via Hangfire;
/// failed delivery relies on Hangfire's built-in automatic retry, no custom retry logic here.
/// Content (subject/body) is computed by the triggering handler, not this job, since the handler
/// already has the entity context in memory.
/// </summary>
public class SendEmailJob(IEmailSender emailSender)
{
    public Task Execute(string toEmail, string subject, string body, CancellationToken cancellationToken) =>
        emailSender.SendAsync(toEmail, subject, body, cancellationToken);
}
