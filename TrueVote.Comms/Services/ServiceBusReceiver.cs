using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using TrueVote.Api;

namespace TrueVote.Comms.Services;

public interface IAzureServiceBusReceiver
{
    Task StartListeningAsync();
    Task CloseAsync();
}

public class AzureServiceBusReceiver : IAzureServiceBusReceiver
{
    private readonly IMessageProcessor _messageProcessor;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusProcessor _serviceBusProcessor;
    private readonly ILogger<AzureServiceBusReceiver> _logger;

    public AzureServiceBusReceiver(IMessageProcessor messageProcessor, ILogger<AzureServiceBusReceiver> logger, string connectionString, string queueName)
    {
        _messageProcessor = messageProcessor;
        _logger = logger;
        _serviceBusClient = new ServiceBusClient(connectionString);
        _serviceBusProcessor = _serviceBusClient.CreateProcessor(queueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1
        });
    }

    public async Task StartListeningAsync()
    {
        _serviceBusProcessor.ProcessMessageAsync += async args =>
        {
            try
            {
                var message = args.Message.Body.ToObjectFromJson<ServiceBusCommsMessage>();

                await _messageProcessor.ProcessMessageAsync(message!);

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AzureServiceBusReceiver->Error processing message");
                throw; // Let Service Bus handle retry
            }
        };

        _serviceBusProcessor.ProcessErrorAsync += ExceptionHandler;
        await _serviceBusProcessor.StartProcessingAsync();
    }

    public async Task CloseAsync()
    {
        await _serviceBusProcessor.StopProcessingAsync();
        await _serviceBusProcessor.CloseAsync();
        await _serviceBusClient.DisposeAsync();
    }

    private Task ExceptionHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "AzureServiceBusReceiver->Service Bus processing error");
        return Task.CompletedTask;
    }
}
