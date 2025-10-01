using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using Karage.Functions.Services;

namespace Karage.Functions.Functions;

public class TamaraFunctions
{
    private readonly ILogger<TamaraFunctions> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _connectionString;
    private readonly ISessionAuthService _sessionAuthService;
    
    // Environment variables
    private readonly string _tamaraApiUrl;
    private readonly string _tamaraAuthToken;
    private readonly string _tamaraNotificationToken;

    // SMS Environment variables
    private readonly string _unifonicApiUrl;
    private readonly string _appSid;
    private readonly string _unifonicUsername;
    private readonly string _unifonicPassword;

    public TamaraFunctions(ILogger<TamaraFunctions> logger, HttpClient httpClient, IConfiguration configuration, ISessionAuthService sessionAuthService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _connectionString = configuration.GetConnectionString("V1DatabaseConnectionString") ?? "";
        _sessionAuthService = sessionAuthService;
        
        // Load environment variables
        _tamaraApiUrl = Environment.GetEnvironmentVariable("TAMARA_API_URL") ?? "https://api-sandbox.tamara.co/";
        _tamaraAuthToken = Environment.GetEnvironmentVariable("TAMARA_AUTH_TOKEN") ?? "";
        _tamaraNotificationToken = Environment.GetEnvironmentVariable("TAMARA_NOTIFICATION_TOKEN") ?? "";

        // Load SMS environment variables
        _unifonicApiUrl = Environment.GetEnvironmentVariable("UNIFONIC_API_URL") ?? "https://api.unifonic.com/rest/SMS/messages";
        _appSid = Environment.GetEnvironmentVariable("APP_SID") ?? "";
        _unifonicUsername = Environment.GetEnvironmentVariable("UNIFONIC_USERNAME") ?? "";
        _unifonicPassword = Environment.GetEnvironmentVariable("UNIFONIC_PASSWORD") ?? "";
    }

    [Function("tamara-webhook")]
    [OpenApiOperation(operationId: "TamaraWebhook", tags: new[] { "Tamara" }, Summary = "Tamara webhook endpoint", Description = "Receives webhook events from Tamara payment service")]
    [OpenApiParameter(name: "tamaraToken", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "JWT token from Tamara for webhook authentication")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(TamaraWebhookRequest), Required = true, Description = "Tamara webhook event payload")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Webhook processed successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Unsupported event type")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object), Description = "Invalid or missing tamaraToken")]
    public async Task<IActionResult> TamaraWebhook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tamara-webhook")] HttpRequest req)
    {
        _logger.LogInformation("Tamara webhook endpoint called.");

        try
        {
            // Get tamaraToken from query params
            var tamaraToken = req.Query["tamaraToken"].FirstOrDefault();
            if (string.IsNullOrEmpty(tamaraToken))
            {
                return new UnauthorizedObjectResult("Missing tamaraToken in query params");
            }

            try
            {
                var secret = _tamaraNotificationToken;
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(secret);
                
                tokenHandler.ValidateToken(tamaraToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = "Tamara",
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var issuer = jwtToken?.Issuer;
                
                if (issuer != "Tamara")
                {
                    return new UnauthorizedObjectResult("Invalid issuer in tamaraToken");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JWT decode failed: {Exception}", ex.Message);
                return new UnauthorizedObjectResult("Invalid tamaraToken format or issuer");
            }
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            JsonDocument body;
            try
            {
                body = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult(new { success = false, message = "Invalid or missing JSON body" });
            }

            var root = body.RootElement;
            var eventType = root.TryGetProperty("event_type", out var eventTypeElement) ? eventTypeElement.GetString() : null;

            var allowedEvents = new HashSet<string>
            {
                "order_approved",    // 103
                "order_authorised",
                "order_canceled",    // 105
                "order_updated",
                "order_captured",
                "order_refunded"     // 106
            };

            if (string.IsNullOrEmpty(eventType) || !allowedEvents.Contains(eventType))
            {
                return new BadRequestObjectResult(new { success = false, message = "Unsupported event type" });
            }

            var orderId = root.TryGetProperty("order_id", out var orderIdElement) ? orderIdElement.GetString() : null;

            if (eventType == "order_approved")
            {
                UpdateOrderStatusUsingTamara(orderId, 103);
            }
            else if (eventType == "order_canceled")
            {
                UpdateOrderStatusUsingTamara(orderId, 105);
            }
            else if (eventType == "order_refunded")
            {
                UpdateOrderStatusUsingTamara(orderId, 106);
            }
            else
            {
                _logger.LogInformation("Unhandled event type: {EventType}", eventType);
                return new OkObjectResult(new { message = $"Event {eventType} received but not processed" });
            }

            return new OkObjectResult(new { message = $"Received event: {eventType}", data = JsonSerializer.Deserialize<object>(requestBody) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Tamara webhook");
            return new StatusCodeResult(500);
        }
    }

    [Function("create-tamara-session")]
    [OpenApiOperation(operationId: "CreateTamaraSession", tags: new[] { "Tamara" }, Summary = "Create Tamara checkout session", Description = "Creates a Tamara in-store checkout session for payment processing")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "Session token")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(object), Required = true, Description = "Tamara checkout session payload")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Session created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid or missing session token")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid request payload")]
    public async Task<IActionResult> CreateTamaraSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "create-tamara-session")] HttpRequest req)
    {
        _logger.LogInformation("Create Tamara session endpoint called.");

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

            // Extra verification: check main keys exist
            var requiredKeys = new[] { "total_amount", "order_reference_id", "order_number", "items", "additional_data" };
            var missingKeys = new List<string>();

            foreach (var key in requiredKeys)
            {
                if (!root.TryGetProperty(key, out _))
                {
                    missingKeys.Add(key);
                }
            }

            if (missingKeys.Any())
            {
                return new BadRequestObjectResult(new { success = false, message = $"Missing required keys: {string.Join(", ", missingKeys)}" });
            }

            var url = _tamaraApiUrl + "checkout/in-store-session";
            var jsonPayload = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(requestBody));
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_tamaraAuthToken}");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Log the Tamara API response
            _logger.LogInformation("Tamara API Response - Status: {StatusCode}, Content: {ResponseContent}", 
                (int)response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"Tamara API request failed with status {(int)response.StatusCode}";
                _logger.LogError("Tamara session creation failed: {ErrorMessage}", errorMessage);
                return new BadRequestObjectResult(new { success = false, message = errorMessage });
            }

            // Extract numeric part from order_reference_id (e.g., 'REF_2907' -> 2907)
            var orderReferenceId = root.TryGetProperty("order_reference_id", out var orderRefElement) ? orderRefElement.GetString() : "";
            int? orderId = null;
            try
            {
                if (!string.IsNullOrEmpty(orderReferenceId))
                {
                    var numericPart = new string(orderReferenceId.Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(numericPart))
                    {
                        orderId = int.Parse(numericPart);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract order ID from reference: {OrderReference}", orderReferenceId);
            }

            // Insert into TamaraOrders table if possible
            try
            {
                var responseJson = JsonDocument.Parse(responseContent);
                var tamaraCheckoutId = responseJson.RootElement.TryGetProperty("checkout_id", out var checkoutElement) ? checkoutElement.GetString() : null;
                var tamaraOrderId = responseJson.RootElement.TryGetProperty("order_id", out var orderElement) ? orderElement.GetString() : null;

                // Save to DB if all values present
                if (orderId.HasValue && !string.IsNullOrEmpty(tamaraOrderId) && !string.IsNullOrEmpty(tamaraCheckoutId))
                {
                    using var connection = new SqlConnection(_connectionString);
                    await connection.OpenAsync();

                    var sql = @"
                        INSERT INTO IntegrationTamaraOrders (OrderID, TamaraOrderID, TamaraCheckoutID)
                        VALUES (@OrderID, @TamaraOrderID, @TamaraCheckoutID)";

                    using var command = new SqlCommand(sql, connection);
                    command.Parameters.AddWithValue("@OrderID", orderId.Value);
                    command.Parameters.AddWithValue("@TamaraOrderID", tamaraOrderId);
                    command.Parameters.AddWithValue("@TamaraCheckoutID", tamaraCheckoutId);

                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Successfully saved Tamara order to database - OrderID: {OrderId}, TamaraOrderID: {TamaraOrderId}",
                        orderId.Value, tamaraOrderId);
                }
                else
                {
                    _logger.LogWarning("Missing values for TamaraOrders insert: order_id={OrderId}, tamara_order_id={TamaraOrderId}, tamara_checkout_id={TamaraCheckoutId}",
                        orderId, tamaraOrderId, tamaraCheckoutId);
                }

                // Send SMS with checkout URL if phone number is provided and valid
                var phoneNumber = root.TryGetProperty("phone_number", out var phoneElement) ? phoneElement.GetString() : null;
                var checkoutUrl = responseJson.RootElement.TryGetProperty("checkout_deeplink", out var urlElement) ? urlElement.GetString() : null;

                if (!string.IsNullOrEmpty(phoneNumber) && !string.IsNullOrEmpty(checkoutUrl) && IsValidSaudiPhoneNumber(phoneNumber))
                {
                    try
                    {
                        // Normalize phone number to international format for SMS
                        var normalizedPhone = NormalizePhoneNumber(phoneNumber);
                        await SendTamaraCheckoutSms(normalizedPhone, checkoutUrl);
                        _logger.LogInformation("SMS sent successfully to {PhoneNumber} for checkout {CheckoutId}", normalizedPhone, tamaraCheckoutId);
                    }
                    catch (Exception smsEx)
                    {
                        _logger.LogError(smsEx, "Failed to send SMS to {PhoneNumber} for checkout {CheckoutId}", phoneNumber, tamaraCheckoutId);
                    }
                }
                else if (!string.IsNullOrEmpty(phoneNumber) && !IsValidSaudiPhoneNumber(phoneNumber))
                {
                    _logger.LogWarning("Invalid Saudi phone number format: {PhoneNumber}", phoneNumber);
                }

                return new OkObjectResult(new { success = true, message = "Tamara session created successfully", checkout_id = tamaraCheckoutId, order_id = tamaraOrderId, checkout_deeplink = checkoutUrl });
            }
            catch (JsonException jsonEx)
            {
                var errorMessage = "Failed to parse Tamara API response";
                _logger.LogError(jsonEx, "{ErrorMessage}: {ResponseContent}", errorMessage, responseContent);
                return new BadRequestObjectResult(new { success = false, message = errorMessage });
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to insert TamaraOrders to database");
                return new OkObjectResult(new { success = true, message = "Tamara session created successfully but failed to save to database", warning = "Database save failed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Tamara session");
            return new ObjectResult(new { success = false, message = "Internal server error occurred while creating Tamara session" }) { StatusCode = 500 };
        }
    }

    private void UpdateOrderStatusUsingTamara(string? orderId, int orderStatus)
    {
        if (string.IsNullOrEmpty(orderId))
        {
            _logger.LogWarning("TamaraOrderID is null or empty, cannot update order status");
            return;
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            // Update Orders table via IntegrationTamaraOrders mapping
            var query = @"
                UPDATE Orders
                SET StatusID = @OrderStatus
                WHERE OrderID = (
                    SELECT OrderID
                    FROM IntegrationTamaraOrders
                    WHERE TamaraOrderID = @TamaraOrderID
                )";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@OrderStatus", orderStatus);
            command.Parameters.AddWithValue("@TamaraOrderID", orderId);
            var rowsAffected = command.ExecuteNonQuery();

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Orders table updated for TamaraOrderID={TamaraOrderId} to StatusID={OrderStatus}", orderId, orderStatus);
            }
            else
            {
                _logger.LogWarning("No orders updated for TamaraOrderID={TamaraOrderId}", orderId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Orders table for TamaraOrderID={TamaraOrderId}", orderId);
            throw;
        }
    }

    private async Task SendTamaraCheckoutSms(string recipient, string checkoutUrl)
    {
        var message = $"Complete your Tamara payment: {checkoutUrl}";

        var payload = new List<KeyValuePair<string, string>>
        {
            new("AppSid", _appSid),
            new("Body", message),
            new("Recipient", recipient),
            new("SenderID", "Karage"),
            new("responseType", "json"),
            new("MessageType", "3")
        };

        var formContent = new FormUrlEncodedContent(payload);

        // Set basic authentication
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_unifonicUsername}:{_unifonicPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);

        var response = await _httpClient.PostAsync(_unifonicApiUrl, formContent);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Clear auth header for next request
        _httpClient.DefaultRequestHeaders.Authorization = null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("SMS API request failed with status {StatusCode}, response: {ResponseContent}",
                (int)response.StatusCode, responseContent);
            throw new Exception($"SMS API request failed with status {(int)response.StatusCode}");
        }

        var responseJson = JsonDocument.Parse(responseContent).RootElement;
        var success = responseJson.TryGetProperty("success", out var successElement) && successElement.GetBoolean();

        if (!success)
        {
            var errorMessage = responseJson.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "Unknown error";
            _logger.LogError("SMS sending failed: {ErrorMessage}", errorMessage);
            throw new Exception($"SMS sending failed: {errorMessage}");
        }
    }

    private static bool IsValidSaudiPhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone))
            return false;

        // Remove any whitespace
        phone = phone.Trim();

        // Handle both +966 and 00966 formats
        string numberPart;
        if (phone.StartsWith("+966"))
        {
            numberPart = phone[4..];
        }
        else if (phone.StartsWith("00966"))
        {
            numberPart = phone[5..];
        }
        else
        {
            return false;
        }

        // Should be exactly 9 digits after country code
        if (numberPart.Length == 9 && numberPart.All(char.IsDigit))
        {
            // Saudi mobile numbers start with 5 after the country code
            return numberPart.StartsWith('5');
        }

        return false;
    }

    private static string NormalizePhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone))
            return phone;

        phone = phone.Trim();

        // Convert 00966 to +966 format for SMS API
        if (phone.StartsWith("00966"))
        {
            return "+966" + phone[5..];
        }

        // Already in +966 format
        if (phone.StartsWith("+966"))
        {
            return phone;
        }

        return phone;
    }
}

// OpenAPI Models for Tamara functions
public class TamaraWebhookRequest
{
    public string event_type { get; set; } = string.Empty;
    public string order_id { get; set; } = string.Empty;
}