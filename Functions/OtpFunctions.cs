using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Karage.Functions.Services;
using System.Net;
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
    [OpenApiOperation(operationId: "SendOtp", tags: new[] { "OTP" }, Summary = "Send OTP via SMS", Description = "Sends a one-time password to the specified phone number via Unifonic SMS service")]
    [OpenApiSecurity("X-Auth-Token", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-Auth-Token")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(OtpRequest), Required = true, Description = "OTP request containing recipient phone number")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(OtpSuccessResponse), Description = "OTP sent successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(OtpErrorResponse), Description = "Invalid request or SMS service error")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing authentication token")]
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

            // Check if Unifonic API call was successful
            var success = sendResult.GetProperty("success").GetBoolean();

            if (success)
            {
                _logger.LogInformation("OTP sent successfully to {Recipient}", recipient);
                return new OkObjectResult(new
                {
                    success = true,
                    otp = otpCode
                });
            }
            else
            {
                // Log Unifonic error details
                var errorMessage = sendResult.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "Unknown error";
                var errorCode = sendResult.TryGetProperty("errorCode", out var errorElement) ? errorElement.GetString() : null;

                _logger.LogError("Unifonic API error - Code: {ErrorCode}, Message: {ErrorMessage}, Recipient: {Recipient}, Full Response: {Response}",
                    errorCode, errorMessage, recipient, sendResult.ToString());

                return new BadRequestObjectResult(new
                {
                    success = false,
                    error = errorMessage
                });
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
            new("SenderID", "Karage"),
            new("responseType", "json")
        };

        var formContent = new FormUrlEncodedContent(payload);

        // Set basic authentication
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_unifonicUsername}:{_unifonicPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

        try
        {
            _logger.LogInformation("Sending OTP request to Unifonic API - Recipient: {Recipient}", recipient);

            var response = await _httpClient.PostAsync(_unifonicApiUrl, formContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Log response status
            _logger.LogInformation("Unifonic API response - Status: {StatusCode}, Content: {ResponseContent}",
                (int)response.StatusCode, responseContent);

            // Clear auth header for next request
            _httpClient.DefaultRequestHeaders.Authorization = null;

            return JsonDocument.Parse(responseContent).RootElement;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP error calling Unifonic API - Recipient: {Recipient}", recipient);

            // Clear auth header
            _httpClient.DefaultRequestHeaders.Authorization = null;

            // Return error response
            return JsonDocument.Parse("{\"success\":false,\"message\":\"Failed to connect to SMS service\"}").RootElement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Unifonic API - Recipient: {Recipient}", recipient);

            // Clear auth header
            _httpClient.DefaultRequestHeaders.Authorization = null;

            // Return error response
            return JsonDocument.Parse("{\"success\":false,\"message\":\"Unexpected error occurred\"}").RootElement;
        }
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

// OpenAPI Models
public class OtpRequest
{
    public string recipient { get; set; } = string.Empty;
}

public class OtpSuccessResponse
{
    public bool success { get; set; }
    public string otp { get; set; } = string.Empty;
}

public class OtpErrorResponse
{
    public bool success { get; set; }
    public string error { get; set; } = string.Empty;
}