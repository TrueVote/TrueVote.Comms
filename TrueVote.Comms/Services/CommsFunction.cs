using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TrueVote.Api;
using TrueVote.Comms.Bots;

namespace TrueVote.Comms.Services;

public class CommsFunction
{
    private readonly IMessageProcessor _messageProcessor;
    private readonly ILogger<CommsFunction> _logger;
    private readonly TelegramBot _telegramBot;

    public CommsFunction(IMessageProcessor messageProcessor, ILogger<CommsFunction> logger, TelegramBot telegramBot)
    {
        _messageProcessor = messageProcessor;
        _logger = logger;
        _telegramBot = telegramBot;
    }

    [Function("ProcessCommsMessage")]
    public async Task Run([ServiceBusTrigger("%ServiceBusCommsQueueName%", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message)
    {
        try
        {
            if (message.Subject == null)
            {
                var messageBody = message.Body.ToString();
                _logger.LogInformation($"Received message: {messageBody}");
                await _telegramBot.SendChannelMessageAsync(messageBody);
            }
            else
            {
                var commsMessage = message.Body.ToObjectFromJson<ServiceBusCommsMessage>();
                await _messageProcessor.ProcessMessageAsync(commsMessage!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            throw;
        }
    }
}
