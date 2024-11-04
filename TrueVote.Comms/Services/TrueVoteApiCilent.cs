#pragma warning disable CS8604 // Possible null reference argument.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using TrueVote.Api;
using Nostr.Client.Keys;
using Nostr.Client.Messages;

namespace TrueVote.Comms.Services;

public interface IApiClient
{
    Task UpdateCommEventStatus(string communicationEventId, string status, DateTime dateProcessed, string? errorMessage = null);
}

public class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<ApiClient> _logger;
    private readonly string _apiBaseUrl;

    public ApiClient(HttpClient httpClient, IAuthenticationService authService, ILogger<ApiClient> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
        _apiBaseUrl = configuration["BaseApiUrl"] ?? throw new ArgumentNullException("BaseApiUrl configuration is missing");
    }

    public async Task UpdateCommEventStatus(string communicationEventId, string status, DateTime dateProcessed, string? errorMessage = null)
    {
        try
        {
            var token = await _authService.GetAuthTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var communicationEventModel = new CommunicationEventModel
            {
                CommunicationEventId = communicationEventId,
                Status = status,
                DateProcessed = dateProcessed,
                DateUpdated = DateTime.UtcNow,
                ErrorMessage = errorMessage
            };

            var response = await _httpClient.PutAsJsonAsync($"{_apiBaseUrl}/comms/events/updatecommeventstatus", communicationEventModel);

            // Log the actual response content for debugging
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug($"ApiClient->API Response: {response.StatusCode} - {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"ApiClient->Failed to update communication event {communicationEventId}. Status: {response.StatusCode}, Response: {responseContent}");

                // Don't try to deserialize error response if it's not JSON
                if (response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var error = await response.Content.ReadFromJsonAsync<SecureString>();
                    throw new Exception($"API Error: {error?.Value ?? "Unknown error"}");
                }

                throw new Exception($"API Error: {response.StatusCode} - {responseContent}");
            }

            _logger.LogDebug($"ApiClient->Successfully updated communication event: {communicationEventId} to: {status}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ApiClient-?Error in UpdateCommEventStatus for communication event: {communicationEventId}");
            throw;
        }
    }
}

public interface IAuthenticationService
{
    Task<string> GetAuthTokenAsync();
}

public class NostrAuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NostrAuthenticationService> _logger;
    private readonly string _nsec;
    private readonly string _apiBaseUrl;

    public NostrAuthenticationService(HttpClient httpClient, ILogger<NostrAuthenticationService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _nsec = configuration["ServiceAccountNsec"] ?? throw new ArgumentNullException("ServiceAccountNsec configuration is missing");
        _apiBaseUrl = configuration["BaseApiUrl"] ?? throw new ArgumentNullException("BaseApiUrl configuration is missing");
    }

    public async Task<string> GetAuthTokenAsync()
    {
        try
        {
            var signInEventModel = CreateSignInEventModel();
            var response = await SignInToApi(signInEventModel);

            return response != null && response.Token != null ? response.Token : throw new AuthenticationException("Failed to authenticate with API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NostrAuthenticationService->Failed to get authentication token");
            throw;
        }
    }

    private SignInEventModel CreateSignInEventModel()
    {
        // Create and sign Nostr event using nsec
        var keys = NostrPrivateKey.FromBech32(_nsec);
        var content = JsonConvert.SerializeObject(new BaseUserModel
        {
            Email = "comms@truevote.org",
            FullName = "TrueVote Comms Service",
            NostrPubKey = keys.DerivePublicKey().Bech32
        });

        var now = DateTime.UtcNow;

        var nostrEvent = new NostrEvent
        {
            Kind = Nostr.Client.Messages.NostrKind.ShortTextNote,
            Pubkey = keys.DerivePublicKey().Bech32,
            CreatedAt = now,
            Content = content,
            Tags = []
        };

        // Sign the event
        var signedEvent = nostrEvent.Sign(keys) ?? throw new ArgumentException("Cannot sign event");

        var signInEventModel = new SignInEventModel
        {
            Kind = Api.NostrKind._1,
            CreatedAt = now,
            PubKey = keys.DerivePublicKey().Bech32,
            Signature = signedEvent.Sig,
            Content = content
        };

        return signInEventModel;
    }

    private async Task<SignInResponse> SignInToApi(SignInEventModel signInEventModel)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_apiBaseUrl}/user/signin", signInEventModel);
        if (response.IsSuccessStatusCode)
        {
            var signInResponse = await response.Content.ReadFromJsonAsync<SignInResponse>() ?? throw new AuthenticationException("Empty response from API");

            _logger.LogDebug($"NostrAuthenticationService->User authenticated: {signInResponse.User.UserId}");

            return signInResponse;
        }

        var error = await response.Content.ReadFromJsonAsync<SecureString>();
        throw new AuthenticationException(error?.Value ?? "Unknown error from API");
    }
}
#pragma warning restore CS8604 // Possible null reference argument.
