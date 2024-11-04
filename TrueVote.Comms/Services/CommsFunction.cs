using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TrueVote.Api;

namespace TrueVote.Comms.Services;

public class CommsFunction
{
    private readonly IMessageProcessor _messageProcessor;
    private readonly ILogger<CommsFunction> _logger;

    public CommsFunction(IMessageProcessor messageProcessor, ILogger<CommsFunction> logger)
    {
        _messageProcessor = messageProcessor;
        _logger = logger;
    }

    [Function("ProcessCommsMessage")]
    public async Task Run([ServiceBusTrigger("%ServiceBusCommsQueueName%", Connection = "ServiceBusConnectionString")] ServiceBusCommsMessage message)
    {
        _logger.LogInformation($"Processing message for communication event: {message.Metadata["CommunicationEventId"]}");

        await _messageProcessor.ProcessMessageAsync(message);
    }
}
