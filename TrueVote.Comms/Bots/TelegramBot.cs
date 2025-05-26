#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable IDE0058 // Expression value is never used
#pragma warning disable CS0618 // Type or member is obsolete

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using TrueVote.Api;
using Telegram.Bot.Polling;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.Json;

// TODO Localize this service, since it returns English messages to Telegram
// See local.settings.json for local settings and Azure Portal for production settings
namespace TrueVote.Comms.Bots
{
    [ExcludeFromCodeCoverage] // TODO Write tests. This requires mocking the Telegram API
    public class TelegramBot
    {
        private static HttpClientHandler? httpClientHandler;
        private static TelegramBotClient? botClient = null; // To connect to bot: https://t.me/TrueVoteAPI_bot
        private static string TelegramRuntimeChannel = string.Empty;
        private static string BaseApiUrl = string.Empty;
        private static readonly string HelpText = "📖 TrueVote API Comms enables you execute some commands on the API. Simply use / in this chat to see a list of commands. To view broadcast messages, be sure and join the TrueVote API Runtime Channel: https://t.me/{0}";

        public TelegramBot()
        {
            Init();
        }

        private static void LogInformation(string s)
        {
            // Would have preferred to use a _logger.LogInformation pattern here but had trouble getting Init() below
            // to not detach the _logger variable after the first await call. So for now using Console.WriteLine(), which is crude, but works
            Console.WriteLine(s);
        }

        public async Task Init()
        {
            if (botClient != null) // In case the function is called again, if it's initialized, don't do it again
                return;

            LogInformation("TelegramBot.Init()");

            using var cts = new CancellationTokenSource();

            // List of BotCommands
            var commands = new List<BotCommand>() {
                new() { Command = "help", Description = "📖 View summary of what the bot can do" },
                new() { Command = "ballots", Description = "🖥 View the count of total number of ballots" },
                new() { Command = "elections", Description = "🖥 View the count of total number of elections" },
                new() { Command = "health", Description = "🧑‍⚕️ Check the API health" },
                new() { Command = "status", Description = "🖥 View the API status" },
                new() { Command = "version", Description = "🤖 View the API version" }
            };

            // Get the Bot settings
            var botKey = Environment.GetEnvironmentVariable("TelegramBotKey");
            if (string.IsNullOrEmpty(botKey))
            {
                LogInformation("Error retrieving Telegram BotKey");
                return;
            }

            TelegramRuntimeChannel = Environment.GetEnvironmentVariable("TelegramRuntimeChannel");
            if (string.IsNullOrEmpty(TelegramRuntimeChannel))
            {
                LogInformation("Error retrieving TelegramRuntimeChannel");
                return;
            }

            BaseApiUrl = Environment.GetEnvironmentVariable("BaseApiUrl");
            if (string.IsNullOrEmpty(BaseApiUrl))
            {
                LogInformation("Error retrieving BaseApiUrl");
                return;
            }

            // Setup HttpClient requests to ignore Certificate errors
            httpClientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
            };

            try
            {
                botClient = new TelegramBotClient(botKey);
            }
            catch (Exception e)
            {
                LogInformation($"Error creating Telegram BotClient: {e.Message}");
                return;
            }

            var testKey = await botClient.TestApi();
            if (!testKey)
            {
                LogInformation($"Error with Telegram Api Key - Failure to connect");
                return;
            }

            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { } // receive all update types
            };

            try
            {
                // Inject the bot with these command options
                var commandStatus = botClient.SetMyCommands(commands, null, null, cts.Token);

                botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cancellationToken: cts.Token);

                var me = await botClient.GetMe();

                LogInformation($"Start listening for @{me.Username}");

                // Disabling notification doesn't seem to work. Too many of these messages.
                //await SendChannelMessageAsync($"TrueVote API Comms Bot Started: @{me.Username}", true);

                // This keeps it running
                new ManualResetEvent(false).WaitOne();

                // Send cancellation request to stop bot
                cts.Cancel();
            }
            catch (Exception e)
            {
                LogInformation($"Error handling Telegram Bot Message: {e.Message}");
                return;
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Only process Message updates: https://core.telegram.org/bots/api#message
            if (update.Type != UpdateType.Message)
                return;

            // Only process text messages
            if (update.Message!.Type != MessageType.Text)
                return;

            var chatId = update.Message.Chat.Id;

            var messageText = update.Message.Text;

            LogInformation($"Type: {update.Type} Received: '{messageText}' message in bot {chatId}.");

            var messageResponse = string.Empty;

            var command = messageText.ToLower().Split(' ').First();

            switch (command)
            {
                case "/help":
                    {
                        messageResponse = string.Format(HelpText, TelegramRuntimeChannel);
                        break;
                    }

                case "/elections":
                    {
                        var ret = await GetElectionsCountAsync();
                        messageResponse = $"Total Elections: {ret}";
                        break;
                    }

                case "/ballots":
                    {
                        var ret = await GetBallotsCountAsync();
                        messageResponse = $"Total Ballots: {ret}";
                        break;
                    }

                case "/health":
                    {
                        var ret = await GetHealthAsync();
                        messageResponse = $"{ret}";
                        break;
                    }

                case "/status":
                    {
                        var ret = await GetStatusAsync();
                        messageResponse = $"{ret}";
                        break;
                    }

                case "/version":
                    {
                        var ret = await GetVersionAsync();
                        messageResponse = $"Version: {ret}";
                        break;
                    }

                default:
                    {
                        break;
                    }
            }

            if (messageResponse.Length > 0)
            {
                var _ = await SendMessageAsync(chatId: chatId, text: messageResponse, cancellationToken: cancellationToken);
                LogInformation($"Sent Message: {messageResponse}");
            }

            // Post command to global group channel
            await SendChannelMessageAsync($"Bot received command: {command} from user: @{update.Message.Chat.Username}");
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            LogInformation($"HandleErrorAsync() Error: {ErrorMessage}");

            return Task.CompletedTask;
        }

        private static async Task<Message> SendMessageAsync(ChatId chatId, string text, CancellationToken cancellationToken, bool disableNotification = false)
        {
            try
            {
                return await botClient.SendMessage(chatId: chatId, text: text, parseMode: ParseMode.None, disableNotification: disableNotification, cancellationToken: cancellationToken);
            }
            catch (Exception e)
            {
                LogInformation($"Error sending Telegram Bot Message: {e.Message}");
                return null;
            }
        }

        public async virtual Task<Message> SendChannelMessageAsync(string text, bool disableNotification = false)
        {
            try
            {
                return await botClient.SendMessage(chatId: $"@{TelegramRuntimeChannel}", text: text, parseMode: ParseMode.None, disableNotification: disableNotification);
            }
            catch (Exception e)
            {
                LogInformation($"Error sending Telegram Channel Message: {e.Message}");
                return null;
            }
        }

        private static string BuildQueryString(object obj)
        {
            var json = JsonConvert.SerializeObject(obj);
            var jsonObject = JObject.Parse(json);
            return string.Join("&", jsonObject.Properties()
                .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value.ToString())}"));
        }

        private static async Task<HttpResponseMessage> SendGetRequestAsync<T>(string endpoint, T requestObject)
        {
            var client = new HttpClient(httpClientHandler);

            var queryString = BuildQueryString(requestObject);
            var fullUrl = $"{BaseApiUrl}/{endpoint}?{queryString}";
            var httpRequestMessage = new HttpRequestMessage
            {
                RequestUri = new Uri(fullUrl),
                Method = HttpMethod.Get
            };

            return await client.SendAsync(httpRequestMessage);
        }

        private static async Task<string> GetElectionsCountAsync()
        {
            try
            {
                var findElectionObj = new FindElectionModel { Name = "All" };
                var ret = await SendGetRequestAsync("election/find", findElectionObj);
                if (ret.StatusCode == HttpStatusCode.NotFound)
                {
                    return "0";
                }

                var retList = await ret.Content.ReadAsAsync<ElectionModelList>();

                return retList.Elections.Count.ToString();
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        private static async Task<string> GetBallotsCountAsync()
        {
            try
            {
                var countBallotsObj = new CountBallotModel { DateCreatedStart = new DateTime(2023, 01, 01), DateCreatedEnd = new DateTime(2033, 12, 31) };
                var ret = await SendGetRequestAsync("ballot/count", countBallotsObj);

                var retCount = await ret.Content.ReadAsAsync<CountBallotModelResponse>();

                return retCount.BallotCount.ToString();
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        private static async Task<string> GetHealthAsync()
        {
            try
            {
                var client = new HttpClient(httpClientHandler);

                var ret = await client.GetAsync($"{BaseApiUrl}/health");

                var result = await ret.Content.ReadAsStringAsync();

                var sresult = result.FormatJson();

                return sresult;
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        private static async Task<string> GetStatusAsync()
        {
            try
            {
                var client = new HttpClient(httpClientHandler);

                var ret = await client.GetAsync($"{BaseApiUrl}/status");

                var result = await ret.Content.ReadAsAsync<StatusModel>();

                // Convert it back to string
                var sresult = JsonConvert.SerializeObject(result, Formatting.Indented);

                return sresult;
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        // TODO Need to really get version from assembly info. Better than Git tag
        private static async Task<string> GetVersionAsync()
        {
            try
            {
                var client = new HttpClient(httpClientHandler);

                var ret = await client.GetAsync($"{BaseApiUrl}/status");

                var result = await ret.Content.ReadAsAsync<StatusModel>();

                return result.BuildInfo.LastTag;
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }

    public static class Extensions
    {
        public static string FormatJson(this string json)
        {
            var options = new JsonSerializerOptions()
            {
                WriteIndented = true
            };

            var jsonElement = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(json);

            return System.Text.Json.JsonSerializer.Serialize(jsonElement, options);
        }
    }
}
#pragma warning restore CS8601 // Possible null reference assignment.
#pragma warning restore CS8603 // Possible null reference return.
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning restore IDE0058 // Expression value is never used
#pragma warning restore CS0618 // Type or member is obsolete
