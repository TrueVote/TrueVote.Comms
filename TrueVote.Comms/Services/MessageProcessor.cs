#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable CS8601 // Possible null reference assignment.
using Microsoft.Extensions.Logging;
using TrueVote.Api;
using TrueVote.Comms.Services;

public interface IMessageProcessor
{
    Task ProcessMessageAsync(ServiceBusCommsMessage message);
}

public class MessageProcessor : IMessageProcessor
{
    private readonly ICommsHandler _commsHandler;
    private readonly ILogger<MessageProcessor> _logger;

    public MessageProcessor(ICommsHandler commsHandler, ILogger<MessageProcessor> logger)
    {
        _commsHandler = commsHandler;
        _logger = logger;
    }

    public async Task ProcessMessageAsync(ServiceBusCommsMessage message)
    {
        _logger.LogInformation($"Processing message for communication event: {message.Metadata["CommunicationEventId"]}");

        switch (message.Metadata["Type"])
        {
            case "VoterAccessCode":
                await ProcessVoterAccessCode(message);
                break;

            default:
                throw new ArgumentException($"Unknown message type: {message.Metadata["Type"]}");
        }
    }

    private async Task ProcessVoterAccessCode(ServiceBusCommsMessage message)
    {
        if (!ValidateVoterAccessCodeMessage(message, out var email, out var accessCode, out var electionId))
        {
            throw new ArgumentException("Required data missing for VoterAccessCode message");
        }

        await _commsHandler.SendVoterAccessCodeEmail(email, accessCode, electionId,
            message.Metadata["CommunicationEventId"]);
    }

    private bool ValidateVoterAccessCodeMessage(ServiceBusCommsMessage message, out string email, out string accessCode, out string electionId)
    {
        email = string.Empty;
        accessCode = string.Empty;
        electionId = string.Empty;

        return message.CommunicationMethod.TryGetValue("Email", out email) &&
               message.MessageData?.TryGetValue("AccessCode", out accessCode) == true &&
               message.RelatedEntities.TryGetValue("ElectionId", out electionId);
    }
}
#pragma warning restore IDE0059 // Unnecessary assignment of a value
#pragma warning restore CS8601 // Possible null reference assignment.
