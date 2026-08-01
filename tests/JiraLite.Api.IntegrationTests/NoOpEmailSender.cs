using JiraLite.Api.Common.Infrastructure.Email;

namespace JiraLite.Api.IntegrationTests;

/// <summary>
/// Swallows outbound mail so the test host never opens an SMTP socket.
///
/// Registered in place of SmtpEmailSender by <see cref="JiraLiteApiFactory"/>. Without it, every
/// invitation and notification enqueues a real SendEmailJob that Hangfire then executes against a
/// nonexistent SMTP server, fails with SocketException 10061, and retries ten times with backoff —
/// filling the test log with stack traces long after the test that triggered it has finished.
///
/// Deliberately stateless: the shared SQL Server container means one factory serves many test
/// classes, so anything recorded here would leak across tests that DatabaseResetHelper cannot
/// clean. A test needing to assert on delivery should inject its own recording sender, the way
/// SendEmailJobTests already does.
/// </summary>
public sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
