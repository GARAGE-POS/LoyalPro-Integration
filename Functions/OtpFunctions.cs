using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Karage.Functions.Services;
using System.Text;
using System.Text.Json;

namespace Karage.Functions.Functions;

public class OtpFunctions
{
    private readonly ILogger<OtpFunctions> _logger;
    private readonly HttpClient _httpClient;
    
    // Environment variables
    private readonly string _apiSecretToken;
    private readonly string _unifonicApiUrl;
    private readonly string _appSid;
    private readonly string _unifonicUsername;
    private readonly string _unifonicPassword;

    public OtpFunctions(ILogger<OtpFunctions> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        
        // Load environment variables
        _apiSecretToken = Environment.GetEnvironmentVariable("API_SECRET_TOKEN") ?? "";
        _unifonicApiUrl = Environment.GetEnvironmentVariable("UNIFONIC_API_URL") ?? "https://api.unifonic.com/rest/SMS/messages";
        _appSid = Environment.GetEnvironmentVariable("APP_SID") ?? "";
        _unifonicUsername = Environment.GetEnvironmentVariable("UNIFONIC_USERNAME") ?? "";
        _unifonicPassword = Environment.GetEnvironmentVariable("UNIFONIC_PASSWORD") ?? "";
    }

    [Function("sendotp")]
    public async Task<IActionResult> SendOtp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sendotp")] HttpRequest req)
    {
        _logger.LogInformation("Send OTP endpoint called.");

        try
        {
            // Token-based protection
            var token = req.Headers["X-Auth-Token"].FirstOrDefault();
            if (token != _apiSecretToken)
            {
                return new UnauthorizedObjectResult("Unauthorized: Invalid or missing token.");
            }

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid request body.");
            }

            var root = doc.RootElement;

            // Get recipient
            if (!root.TryGetProperty("recipient", out var recipientElement))
            {
                return new BadRequestObjectResult("Missing recipient.");
            }

            var recipient = recipientElement.GetString();

            // Validate recipient exists and is a valid phone number (digits only, 10-15 digits)
            if (string.IsNullOrEmpty(recipient))
            {
                return new BadRequestObjectResult("Missing recipient.");
            }

            if (!IsValidPhoneNumber(recipient))
            {
                return new BadRequestObjectResult("Recipient must be a valid phone number in international format (digits only, 10-15 digits).");
            }

            // Generate OTP and send
            var otpCode = GenerateOtp();
            var sendResult = await SendOtpRequest(recipient, otpCode);

            // Prepare response fields
            var success = sendResult.GetProperty("success").GetBoolean();
            var message = sendResult.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : null;
            var errorCode = sendResult.TryGetProperty("errorCode", out var errorElement) ? errorElement.GetString() : null;
            
            var data = new Dictionary<string, object>();
            if (sendResult.TryGetProperty("data", out var dataElement))
            {
                foreach (var property in dataElement.EnumerateObject())
                {
                    data[property.Name] = property.Value.GetRawText().Trim('"');
                }
            }

            // Add OTP to data for demo/testing purposes (remove in production)
            data["otp"] = otpCode;

            var responseJson = new
            {
                success = success,
                message = message,
                errorCode = errorCode,
                data = data
            };

            var statusCode = success ? 200 : 400;
            
            if (statusCode == 200)
            {
                return new OkObjectResult(responseJson);
            }
            else
            {
                return new BadRequestObjectResult(responseJson);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OTP");
            return new StatusCodeResult(500);
        }
    }

    private string GenerateOtp(int length = 6)
    {
        var random = new Random();
        var min = (int)Math.Pow(10, length - 1);
        var max = (int)Math.Pow(10, length) - 1;
        return random.Next(min, max + 1).ToString();
    }

    private async Task<JsonElement> SendOtpRequest(string recipient, string otp)
    {
        var payload = new List<KeyValuePair<string, string>>
        {
            new("AppSid", _appSid),
            new("Body", $"Karage OTP is: {otp}"),
            new("Recipient", recipient),
            new("responseType", "json")
        };

        var formContent = new FormUrlEncodedContent(payload);
        
        // Set basic authentication
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_unifonicUsername}:{_unifonicPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

        var response = await _httpClient.PostAsync(_unifonicApiUrl, formContent);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        // Clear auth header for next request
        _httpClient.DefaultRequestHeaders.Authorization = null;
        
        return JsonDocument.Parse(responseContent).RootElement;
    }

    private bool IsValidPhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone))
            return false;

        // Check if contains only digits
        if (!phone.All(char.IsDigit))
            return false;

        // Check length (10-15 digits)
        return phone.Length >= 10 && phone.Length <= 15;
    }
}