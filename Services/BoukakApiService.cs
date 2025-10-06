using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Karage.Functions.Services;

public interface IBoukakApiService
{
    Task<BoukakCustomerCardResponse?> CreateCustomerCardAsync(BoukakCustomerCardRequest request);
    Task<BoukakAddStampsResponse?> AddStampsAsync(BoukakAddStampsRequest request);
}

// Request models
public class BoukakCustomerCardRequest
{
    public string templateId { get; set; } = string.Empty;
    public string platform { get; set; } = "android"; // "android" or "iOS"
    public string language { get; set; } = "en"; // "en" or "ar"
    public BoukakCustomerData customerData { get; set; } = new();
}

public class BoukakCustomerData
{
    public string? firstname { get; set; }
    public string? lastname { get; set; }
    public string? phone { get; set; }
    public string? dob { get; set; } // ISO 8601 format: "1998-02-18T00:00:00.000Z"
    public string? gender { get; set; }
    public string? email { get; set; }
    public decimal? initialCashback { get; set; }
}

public class BoukakAddStampsRequest
{
    public string cardId { get; set; } = string.Empty;
    public int stamps { get; set; }
    public BoukakProductInfo? products { get; set; }
}

public class BoukakProductInfo
{
    public string? name { get; set; }
    public decimal? price { get; set; }
}

// Response models
public class BoukakCustomerCardResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public string? applePassUrl { get; set; }
    public string? passWalletUrl { get; set; }
    public string? customerId { get; set; } // From x-customer-id header
    public string? cardId { get; set; } // From x-card-id header
}

public class BoukakAddStampsResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public int activeStamps { get; set; }
    public int rewards { get; set; }
}

// Webhook models
public class BoukakWebhookPayload
{
    public string @event { get; set; } = string.Empty; // CARD_INSTALLED, CARD_UNINSTALLED
    public BoukakWebhookData? data { get; set; }
}

public class BoukakWebhookData
{
    public string? cardId { get; set; }
    public string? customerId { get; set; }
}

public class BoukakApiService : IBoukakApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BoukakApiService> _logger;
    private const string SandboxBaseUrl = "https://sandbox.api.partners.boukak.com";
    private const string ProductionBaseUrl = "https://api.partners.boukak.com";
    private const string ApiKey = "vTf8du7MwXm/0nu+0y732/hoxYlTirreZoSfiqEu/43sRKmkB+Lczo++dXt0Px7bJ4gTxSeFSDE7DHbo/rO1PFr0BUTSDM+/XGHbMwl8aPmk1b0o85D/12RnSl6JYUM0RN6FZhMtcY/J5WA6ZP7UAjHyODv/JLTINywDcO1TRmtvIFAlzS5QdJouo56sBuWfHKdJA9tX8BeKodMZJV6379CNA9vtd6revk92r2RS9rKC9yfhkxLgoXLBfn/1t/ZbX3wvwKVqpE/gelPpK/qmTA==";

    // Use production environment by default
    private string BaseUrl => ProductionBaseUrl;

    public BoukakApiService(HttpClient httpClient, ILogger<BoukakApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BoukakCustomerCardResponse?> CreateCustomerCardAsync(BoukakCustomerCardRequest request)
    {
        try
        {
            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Creating Boukak customer card with data: {Data}", jsonContent);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/create-customer-card");
            httpRequest.Content = content;
            httpRequest.Headers.Add("api-key", ApiKey);
            httpRequest.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Boukak customer card created successfully. Response: {Response}", responseContent);

                var cardResponse = JsonSerializer.Deserialize<BoukakCustomerCardResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (cardResponse != null)
                {
                    // Extract customer ID and card ID from response headers
                    if (response.Headers.TryGetValues("x-customer-id", out var customerIdValues))
                    {
                        cardResponse.customerId = customerIdValues.FirstOrDefault();
                    }

                    if (response.Headers.TryGetValues("x-card-id", out var cardIdValues))
                    {
                        cardResponse.cardId = cardIdValues.FirstOrDefault();
                    }

                    _logger.LogInformation("Extracted Boukak IDs - CustomerId: {CustomerId}, CardId: {CardId}",
                        cardResponse.customerId, cardResponse.cardId);
                }

                return cardResponse;
            }

            _logger.LogError("Failed to create Boukak customer card. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while creating Boukak customer card");
            return null;
        }
    }

    public async Task<BoukakAddStampsResponse?> AddStampsAsync(BoukakAddStampsRequest request)
    {
        try
        {
            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Adding stamps to Boukak card {CardId}: {Data}", request.cardId, jsonContent);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/add-stamps");
            httpRequest.Content = content;
            httpRequest.Headers.Add("api-key", ApiKey);
            httpRequest.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Stamps added successfully to Boukak card. Response: {Response}", responseContent);

                var stampsResponse = JsonSerializer.Deserialize<BoukakAddStampsResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return stampsResponse;
            }

            _logger.LogError("Failed to add stamps to Boukak card. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while adding stamps to Boukak card");
            return null;
        }
    }
}
