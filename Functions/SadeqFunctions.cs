using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Karage.Functions.Functions;

public class SadeqFunctions
{
    private readonly ILogger<SadeqFunctions> _logger;
    private readonly HttpClient _httpClient;
    
    // Environment variables
    private readonly string _sadeqUrl;
    private readonly string _sadeqUsername;
    private readonly string _sadeqPassword;
    private readonly string _sadeqAccountId;
    private readonly string _sadeqAccountSecret;
    private readonly string _sadeqRequestUsername;
    private readonly string _sadeqRequestPassword;
    private readonly string _templateId;

    public SadeqFunctions(ILogger<SadeqFunctions> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        
        // Load environment variables
        _sadeqUrl = Environment.GetEnvironmentVariable("SADQ_URL") ?? "";
        _sadeqUsername = Environment.GetEnvironmentVariable("SADQ_USERNAME") ?? "";
        _sadeqPassword = Environment.GetEnvironmentVariable("SADQ_PASSWORD") ?? "";
        _sadeqAccountId = Environment.GetEnvironmentVariable("SADQ_ACCOUNT_ID") ?? "";
        _sadeqAccountSecret = Environment.GetEnvironmentVariable("SADQ_ACCOUNT_SECRET") ?? "";
        _sadeqRequestUsername = Environment.GetEnvironmentVariable("SADQ_REQUEST_USERNAME") ?? "";
        _sadeqRequestPassword = Environment.GetEnvironmentVariable("SADQ_REQUEST_PASSWORD") ?? "";
        _templateId = Environment.GetEnvironmentVariable("TEMPLATE_ID") ?? "";
    }

    [Function("sadeq_request")]
    public async Task<IActionResult> SadeqRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sadeq_request")] HttpRequest req)
    {
        _logger.LogInformation("Sadeq request endpoint called.");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid JSON body.");
            }

            var root = doc.RootElement;

            // Check required fields
            var requiredFields = new[] { "destinationName", "destinationEmail", "destinationPhoneNumber", "nationalId" };
            var missingFields = new List<string>();

            foreach (var field in requiredFields)
            {
                if (!root.TryGetProperty(field, out var element) || string.IsNullOrEmpty(element.GetString()))
                {
                    missingFields.Add(field);
                }
            }

            if (missingFields.Any())
            {
                return new BadRequestObjectResult($"Missing required fields: {string.Join(", ", missingFields)}");
            }

            // Extract values
            var destinationName = root.GetProperty("destinationName").GetString()!;
            var destinationEmail = root.GetProperty("destinationEmail").GetString()!;
            var destinationPhoneNumber = root.GetProperty("destinationPhoneNumber").GetString()!;
            var nationalId = root.GetProperty("nationalId").GetString()!;

            // Execute the workflow: get token, initiate envelope, send invitation
            var accessToken = await GetSadeqToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                return new ObjectResult("Failed to obtain access token.") { StatusCode = 500 };
            }

            var documentId = await InitiateEnvelopeByTemplate(accessToken);
            if (string.IsNullOrEmpty(documentId))
            {
                return new ObjectResult("Failed to obtain document ID.") { StatusCode = 500 };
            }

            var url = $"{_sadeqUrl}/IntegrationService/Invitation/Send-Invitation";
            var payload = new
            {
                documentId = documentId,
                destinations = new[]
                {
                    new
                    {
                        destinationName = destinationName,
                        destinationEmail = destinationEmail,
                        destinationPhoneNumber = destinationPhoneNumber,
                        nationalId = nationalId,
                        signeOrder = 0,
                        ConsentOnly = true,
                        signatories = new object[] { },
                        availableTo = GetDate30DaysFromNow(),
                        allowUserToSignAnyWhere = false,
                        authenticationType = 7,
                        invitationLanguage = 1,
                        redirectUrl = "",
                        allowUserToAddDestination = false
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            return new ContentResult
            {
                Content = responseContent,
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sadeq request");
            return new StatusCodeResult(500);
        }
    }

    private async Task<string?> GetSadeqToken()
    {
        var url = $"{_sadeqUrl}/Authentication/Authority/Token";
        
        // Check if critical variables are missing
        if (string.IsNullOrEmpty(_sadeqRequestUsername) || string.IsNullOrEmpty(_sadeqRequestPassword))
        {
            _logger.LogError("Missing critical environment variables: REQUEST_USERNAME={RequestUsername}, REQUEST_PASSWORD={PasswordSet}", 
                _sadeqRequestUsername, string.IsNullOrEmpty(_sadeqRequestPassword) ? "None" : "set");
            return null;
        }

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_sadeqUsername}:{_sadeqPassword}"));
        var payload = $"grant_type=integration&password={_sadeqRequestPassword}&username={_sadeqRequestUsername}&accountId={_sadeqAccountId}&accountSecret={_sadeqAccountSecret}";
        
        var content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {basicAuth}");

        var response = await _httpClient.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        try
        {
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
            if (jsonResponse.TryGetProperty("access_token", out var tokenElement))
            {
                return tokenElement.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing token response");
        }

        return null;
    }

    private async Task<string?> InitiateEnvelopeByTemplate(string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogError("Failed to obtain access token.");
            return null;
        }

        var url = $"{_sadeqUrl}/IntegrationService/Document/Initiate-envelope-by-template";
        
        var formData = new MultipartFormDataContent();
        formData.Add(new StringContent(_templateId), "TemplateId");
        formData.Add(new StringContent("false"), "UserOnlySigner");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("accept", "text/plain");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.PostAsync(url, formData);
        var responseContent = await response.Content.ReadAsStringAsync();

        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            // Extract documentId from the nested data structure
            if (result.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.TryGetProperty("documentId", out var documentIdElement))
                {
                    return documentIdElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing envelope response");
        }

        return null;
    }

    private string GetDate30DaysFromNow()
    {
        var futureDate = DateTime.Now.AddDays(30);
        return futureDate.ToString("yyyy-MM-dd");
    }
}