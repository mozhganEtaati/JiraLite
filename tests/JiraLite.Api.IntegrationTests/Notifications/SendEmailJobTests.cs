using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Email;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Notifications;

public class SendEmailJobTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public string? ToEmail { get; private set; }
        public string? Subject { get; private set; }
        public string? Body { get; private set; }

        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
        {
            ToEmail = toEmail;
            Subject = subject;
            Body = body;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Execute_forwards_the_arguments_to_the_email_sender_unchanged()
    {
        var fakeSender = new FakeEmailSender();
        var job = new SendEmailJob(fakeSender);

        await job.Execute("someone@example.com", "Subject line", "Body text", CancellationToken.None);

        Assert.Equal("someone@example.com", fakeSender.ToEmail);
        Assert.Equal("Subject line", fakeSender.Subject);
        Assert.Equal("Body text", fakeSender.Body);
    }
}
