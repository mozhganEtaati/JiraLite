namespace JiraLite.Api.Common.Infrastructure.Email;

/// <summary>spec/20-coding-guidelines.md §8 — SMTP connection settings, bound from configuration, never hardcoded.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 25;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; }
    public string FromAddress { get; set; } = "no-reply@jiralite.dev";
    public string FromName { get; set; } = "JiraLite";
}
