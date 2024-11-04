using Microsoft.Extensions.Logging;

namespace TrueVote.Comms.Services;

public interface ICommsHandler
{
    Task SendVoterAccessCodeEmail(string email, string accessCode, string electionid, string communicationEventId);
    Task SendBallotConfirmationEmail(string email, string ballotId, string communicationEventId);
}

public class CommsHandler : ICommsHandler
{
    private readonly ILogger<CommsHandler> _logger;
    private readonly IEmailService _emailService;
    private readonly IApiClient _apiClient;

    public CommsHandler(ILogger<CommsHandler> logger, IEmailService emailService, IApiClient apiClient)
    {
        _logger = logger;
        _emailService = emailService;
        _apiClient = apiClient;
    }

    public async Task SendVoterAccessCodeEmail(string email, string accessCode, string electionId, string communicationEventId)
    {
        try
        {
            _logger.LogInformation($"CommsHandler->Sending voter access code email for communication event: {communicationEventId}");

            if (string.IsNullOrEmpty(accessCode))
            {
                throw new ArgumentException("Access code cannot be null or empty", nameof(accessCode));
            }

            await _emailService.SendEmailAsync(email, "Your TrueVote Alpha Access Code - Ready to Vote!", EmailTemplate.VoterAccessCode,
                new Dictionary<string, string> { { "EAC", accessCode }, { "ELECTIONID", electionId } });

            await _apiClient.UpdateCommEventStatus(communicationEventId, "Completed", DateTime.UtcNow);

            _logger.LogInformation($"CommsHandler->Successfully sent voter access code email for communication event: {communicationEventId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"CommsHandler->Error sending voter access code email for communication event: {communicationEventId}");

            await _apiClient.UpdateCommEventStatus(communicationEventId, "Failed", DateTime.UtcNow, ex.Message);

            throw;
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task SendBallotConfirmationEmail(string email, string ballotId, string communicationEventId)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        throw new NotImplementedException();
    }
}
