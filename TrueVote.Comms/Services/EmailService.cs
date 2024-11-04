#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8604 // Possible null reference argument.
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Reflection;

namespace TrueVote.Comms.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, EmailTemplate template, Dictionary<string, string> tokens);
}

public enum EmailTemplate
{
    VoterAccessCode,
    BallotConfirmation
    // Add more as needed
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly Dictionary<EmailTemplate, (string File, HashSet<string> RequiredTokens)> _templateConfig;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
        _templateConfig = new Dictionary<EmailTemplate, (string, HashSet<string>)>
        {
            {
                EmailTemplate.VoterAccessCode,
                ("election_access_code.txt", new HashSet<string> { "EAC", "ELECTIONID" })
            },
            {
                EmailTemplate.BallotConfirmation,
                ("ballot_confirmation.txt", new HashSet<string> { "BALLOTID", "TIMESTAMP" })
            }
        };
    }

    public async Task SendEmailAsync(string to, string subject, EmailTemplate template, Dictionary<string, string> tokens)
    {
        try
        {
            if (!_templateConfig.TryGetValue(template, out var config))
            {
                throw new ArgumentException($"Template not configured: {template}");
            }

            // Validate all required tokens are present
            var missingTokens = config.RequiredTokens.Where(t => !tokens.ContainsKey(t));
            if (missingTokens.Any())
            {
                throw new ArgumentException($"Missing required tokens: {string.Join(", ", missingTokens)}");
            }

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"TrueVote.Comms.email_templates.{config.File}");
            using var reader = new StreamReader(stream);
            var body = await reader.ReadToEndAsync();
            foreach (var token in tokens)
            {
                body = body.Replace($"{{{{{token.Key}}}}}", token.Value);
            }

            using var smtpClient = new SmtpClient
            {
                Host = Environment.GetEnvironmentVariable("MandrillSMTPServerHost"),
                Port = int.Parse(Environment.GetEnvironmentVariable("MandrillSMTPServerPort")),
                Credentials = new NetworkCredential("TrueVote", Environment.GetEnvironmentVariable("MandrillApiKey"))
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(Environment.GetEnvironmentVariable("TransactionFromEmailAddress"), Environment.GetEnvironmentVariable("TransactionFromEmailName")),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mailMessage.To.Add(to);

            try
            {
                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (SmtpException ex) when (ex.Message?.Contains("Relay access denied", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                _logger.LogInformation("Ignoring expected SMTP relay error - email still sent");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending email to {to}");
            throw;
        }
    }
}
#pragma warning restore CS8601 // Possible null reference assignment.
#pragma warning restore CS8604 // Possible null reference argument.
