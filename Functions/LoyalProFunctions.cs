using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Karage.Functions.Services;

namespace Karage.Functions.Functions;

public class LoyalProFunctions
{
    private readonly ILogger<LoyalProFunctions> _logger;
    private readonly HttpClient _httpClient;
    private readonly ISessionAuthService _sessionAuthService;

    // Environment variables
    private readonly string _loyalProApiUrl;
    private readonly string _loyalProAuthToken;

    public LoyalProFunctions(ILogger<LoyalProFunctions> logger, HttpClient httpClient, ISessionAuthService sessionAuthService, IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _sessionAuthService = sessionAuthService;

        // Load environment variables
        _loyalProApiUrl = Environment.GetEnvironmentVariable("LOYALPRO_API_URL") ?? "https://loyapro.com/api2/garage/";
        _loyalProAuthToken = Environment.GetEnvironmentVariable("LOYALPRO_AUTH_TOKEN") ?? "";
    }

    [Function("loyalpro-reward")]
    [OpenApiOperation(operationId: "LoyalProReward", tags: new[] { "LoyalPro" }, Summary = "LoyalPro reward endpoint", Description = "Sends reward request to LoyalPro API")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "Session token")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(LoyalProRewardRequest), Required = true, Description = "LoyalPro reward request payload")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Reward processed successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid request data")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid or missing session token")]
    public async Task<IActionResult> LoyalProReward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalpro-reward")] HttpRequest req)
    {
        _logger.LogInformation("LoyalPro reward endpoint called.");

        try
        {
            // Verify session authentication
            var (authResult, sessionData) = await _sessionAuthService.VerifySessionAndGetData(req);
            if (authResult != null)
            {
                return authResult; // Return unauthorized if session validation failed
            }

            _logger.LogInformation("Session authenticated for user {UserId} at location {LocationId}",
                sessionData!.UserID, sessionData.LocationID);

            // Extract session token from Authorization header for business_reference
            var authHeader = req.Headers["Authorization"].ToString();
            var sessionToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader.Substring("Bearer ".Length).Trim()
                : string.Empty;

            if (string.IsNullOrEmpty(sessionToken))
            {
                return new BadRequestObjectResult(new { success = false, message = "Session token could not be extracted from Authorization header" });
            }

            // Extract business_reference (POS-XXXXX) from session token
            // Session token format: POS-KARAGE638954291932370545WDD1 or POS-3d6kqv6389603618196563500
            // We need only: POS-KARAGE or POS-3d6kqv (before the long timestamp)
            var businessReference = sessionToken;
            if (sessionToken.StartsWith("POS-", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(sessionToken, @"^(POS-[A-Za-z0-9]+?)(?=\d{10,}|$)");
                if (match.Success)
                {
                    businessReference = match.Groups[1].Value;
                }
            }

            // Parse and validate JSON body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            JsonDocument payload;
            try
            {
                payload = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult(new { success = false, message = "Invalid or missing JSON body" });
            }

            var root = payload.RootElement;

            // Extract required fields
            var branchId = root.TryGetProperty("branch_id", out var branchIdElement) ? branchIdElement.GetString() : null;
            var rewardCode = root.TryGetProperty("reward_code", out var rewardCodeElement) ? rewardCodeElement.GetString() : null;

            if (string.IsNullOrEmpty(branchId) || string.IsNullOrEmpty(rewardCode))
            {
                return new BadRequestObjectResult(new { success = false, message = "branch_id and reward_code are required" });
            }

            // Prepare request to LoyalPro API
            var url = _loyalProApiUrl + "reward";
            var loyalProPayload = new
            {
                business_reference = businessReference,
                branch_id = branchId,
                reward_code = rewardCode
            };

            var jsonPayload = JsonSerializer.Serialize(loyalProPayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_loyalProAuthToken}");

            _logger.LogInformation("Calling LoyalPro API: {Url} with business_reference={BusinessReference}, branch_id={BranchId}, reward_code={RewardCode}",
                url, businessReference, branchId, rewardCode);

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Log the LoyalPro API response
            _logger.LogInformation("LoyalPro API Response - Status: {StatusCode}, Content: {ResponseContent}",
                (int)response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"LoyalPro API request failed with status {(int)response.StatusCode}";
                _logger.LogError("LoyalPro reward request failed: {ErrorMessage}, Response: {ResponseContent}",
                    errorMessage, responseContent);
                return new BadRequestObjectResult(new { success = false, message = errorMessage, details = responseContent });
            }

            // Parse and return the response from LoyalPro
            try
            {
                var responseJson = JsonDocument.Parse(responseContent);
                return new OkObjectResult(new { success = true, data = responseJson.RootElement });
            }
            catch (JsonException)
            {
                // If response is not JSON, return as plain text
                return new OkObjectResult(new { success = true, data = responseContent });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LoyalPro reward request");
            return new ObjectResult(new { success = false, message = "Internal server error occurred while processing LoyalPro reward" }) { StatusCode = 500 };
        }
    }

    [Function("loyalpro-redeem")]
    [OpenApiOperation(operationId: "LoyalProRedeem", tags: new[] { "LoyalPro" }, Summary = "LoyalPro redeem endpoint", Description = "Redeems rewards via LoyalPro API")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "Session token")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(LoyalProRedeemRequest), Required = true, Description = "LoyalPro redeem request payload")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Redeem processed successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid request data")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid or missing session token")]
    public async Task<IActionResult> LoyalProRedeem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "loyalpro-redeem")] HttpRequest req)
    {
        _logger.LogInformation("LoyalPro redeem endpoint called.");

        try
        {
            // Verify session authentication
            var (authResult, sessionData) = await _sessionAuthService.VerifySessionAndGetData(req);
            if (authResult != null)
            {
                return authResult; // Return unauthorized if session validation failed
            }

            _logger.LogInformation("Session authenticated for user {UserId} at location {LocationId}",
                sessionData!.UserID, sessionData.LocationID);

            // Extract session token from Authorization header for business_reference
            var authHeader = req.Headers["Authorization"].ToString();
            var sessionToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authHeader.Substring("Bearer ".Length).Trim()
                : string.Empty;

            if (string.IsNullOrEmpty(sessionToken))
            {
                return new BadRequestObjectResult(new { success = false, message = "Session token could not be extracted from Authorization header" });
            }

            // Extract business_reference (POS-XXXXX) from session token
            // Session token format: POS-KARAGE638954291932370545WDD1 or POS-3d6kqv6389603618196563500
            // We need only: POS-KARAGE or POS-3d6kqv (before the long timestamp)
            var businessReference = sessionToken;
            if (sessionToken.StartsWith("POS-", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(sessionToken, @"^(POS-[A-Za-z0-9]+?)(?=\d{10,}|$)");
                if (match.Success)
                {
                    businessReference = match.Groups[1].Value;
                }
            }

            // Parse and validate JSON body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            JsonDocument payload;
            try
            {
                payload = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult(new { success = false, message = "Invalid or missing JSON body" });
            }

            var root = payload.RootElement;

            // Validate required fields
            var requiredFields = new[] { "discount_amount", "redeemed_products", "user_id", "branch_id", "order_id", "reward_code" };
            var missingFields = new List<string>();

            foreach (var field in requiredFields)
            {
                if (!root.TryGetProperty(field, out _))
                {
                    missingFields.Add(field);
                }
            }

            if (missingFields.Any())
            {
                return new BadRequestObjectResult(new { success = false, message = $"Missing required fields: {string.Join(", ", missingFields)}" });
            }

            // Extract fields from request
            var discountAmount = root.GetProperty("discount_amount").GetDecimal();
            var redeemed_products = root.GetProperty("redeemed_products");
            var userId = root.GetProperty("user_id").GetString();
            var branchId = root.GetProperty("branch_id").GetString();
            var orderId = root.GetProperty("order_id").GetString();
            var rewardCode = root.GetProperty("reward_code").GetString();

            // Handle optional redeemed_combos
            object redeemed_combos_value = Array.Empty<object>();
            if (root.TryGetProperty("redeemed_combos", out var combosElement))
            {
                redeemed_combos_value = JsonSerializer.Deserialize<JsonElement>(combosElement.GetRawText());
            }

            // Prepare request to LoyalPro API with business_reference
            var url = _loyalProApiUrl + "redeem";
            var loyalProPayload = new
            {
                business_reference = businessReference,
                discount_amount = discountAmount,
                redeemed_products = JsonSerializer.Deserialize<JsonElement>(redeemed_products.GetRawText()),
                redeemed_combos = redeemed_combos_value,
                user_id = userId,
                branch_id = branchId,
                order_id = orderId,
                reward_code = rewardCode
            };

            var jsonPayload = JsonSerializer.Serialize(loyalProPayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_loyalProAuthToken}");

            _logger.LogInformation("Calling LoyalPro redeem API: {Url} with business_reference={BusinessReference}, order_id={OrderId}, reward_code={RewardCode}",
                url, businessReference, orderId, rewardCode);

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Log the LoyalPro API response
            _logger.LogInformation("LoyalPro redeem API Response - Status: {StatusCode}, Content: {ResponseContent}",
                (int)response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"LoyalPro API request failed with status {(int)response.StatusCode}";
                _logger.LogError("LoyalPro redeem request failed: {ErrorMessage}, Response: {ResponseContent}",
                    errorMessage, responseContent);
                return new BadRequestObjectResult(new { success = false, message = errorMessage, details = responseContent });
            }

            // Parse and return the response from LoyalPro
            try
            {
                var responseJson = JsonDocument.Parse(responseContent);
                return new OkObjectResult(new { success = true, data = responseJson.RootElement });
            }
            catch (JsonException)
            {
                // If response is not JSON, return as plain text
                return new OkObjectResult(new { success = true, data = responseContent });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LoyalPro redeem request");
            return new ObjectResult(new { success = false, message = "Internal server error occurred while processing LoyalPro redeem" }) { StatusCode = 500 };
        }
    }
}

// OpenAPI Models for LoyalPro functions
public class LoyalProRewardRequest
{
    public string branch_id { get; set; } = string.Empty;
    public string reward_code { get; set; } = string.Empty;
    // Note: business_reference is extracted from Authorization header (session token)
}

public class LoyalProRedeemRequest
{
    public decimal discount_amount { get; set; }
    public List<RedeemedProduct> redeemed_products { get; set; } = new();
    public List<object> redeemed_combos { get; set; } = new();
    public string user_id { get; set; } = string.Empty;
    public string branch_id { get; set; } = string.Empty;
    public string order_id { get; set; } = string.Empty;
    public string reward_code { get; set; } = string.Empty;
    // Note: business_reference is extracted from Authorization header (session token)
}

public class RedeemedProduct
{
    public string id { get; set; } = string.Empty;
    public int quantity { get; set; }
}
