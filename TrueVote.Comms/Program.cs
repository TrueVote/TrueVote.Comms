using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrueVote.Comms.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<ICommsHandler, CommsHandler>();
        services.AddSingleton<IMessageProcessor, MessageProcessor>();
        services.AddSingleton<IAuthenticationService, NostrAuthenticationService>();
        services.AddHttpClient<IApiClient, ApiClient>(client =>
        {
            client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("BaseApiUrl") ?? throw new ArgumentNullException("BaseApiUrl configuration is missing"));
        });
    })
    .Build();

host.Run();
