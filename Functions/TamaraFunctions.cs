using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;

namespace Karage.Functions.Functions;

public class TamaraFunctions
{
    private readonly ILogger<TamaraFunctions> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _connectionString;
    
    // Environment variables
    private readonly string _tamaraApiUrl;
    private readonly string _tamaraAuthToken;
    private readonly string _tamaraNotificationToken;

    public TamaraFunctions(ILogger<TamaraFunctions> logger, HttpClient httpClient, IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _connectionString = configuration.GetConnectionString("V1DatabaseConnectionString") ?? "";
        
        // Load environment variables
        _tamaraApiUrl = Environment.GetEnvironmentVariable("TAMARA_API_URL") ?? "https://api-sandbox.tamara.co/";
        _tamaraAuthToken = Environment.GetEnvironmentVariable("TAMARA_AUTH_TOKEN") ?? "";
        _tamaraNotificationToken = Environment.GetEnvironmentVariable("TAMARA_NOTIFICATION_TOKEN") ?? "";
    }

    [Function("tamara-webhook")]
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
                UpdateOrderCheckoutDetailsUsingTamara(orderId, 103);
            }
            else if (eventType == "order_canceled")
            {
                UpdateOrderCheckoutDetailsUsingTamara(orderId, 105);
            }
            else if (eventType == "order_refunded")
            {
                UpdateOrderCheckoutDetailsUsingTamara(orderId, 106);
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
    public async Task<IActionResult> CreateTamaraSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "create-tamara-session")] HttpRequest req)
    {
        _logger.LogInformation("Create Tamara session endpoint called.");

        try
        {
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
                    var orderCheckoutId = FetchOrderCheckoutDetails(orderId.Value);
                    if (orderCheckoutId.HasValue)
                    {
                        using var connection = new SqlConnection(_connectionString);
                        await connection.OpenAsync();
                        
                        var sql = @"
                            INSERT INTO TamaraOrders (OrderCheckOutID, TamaraOrderID, TamaraCheckOutID)
                            VALUES (@OrderCheckOutID, @TamaraOrderID, @TamaraCheckOutID)";
                        
                        using var command = new SqlCommand(sql, connection);
                        command.Parameters.AddWithValue("@OrderCheckOutID", orderCheckoutId.Value);
                        command.Parameters.AddWithValue("@TamaraOrderID", tamaraOrderId);
                        command.Parameters.AddWithValue("@TamaraCheckOutID", tamaraCheckoutId);
                        
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Successfully saved Tamara order to database - OrderCheckoutID: {OrderCheckoutId}, TamaraOrderID: {TamaraOrderId}", 
                            orderCheckoutId.Value, tamaraOrderId);
                    }
                    else
                    {
                        _logger.LogWarning("OrderCheckoutID not found for OrderID: {OrderId}", orderId.Value);
                    }
                }
                else
                {
                    _logger.LogWarning("Missing values for TamaraOrders insert: order_id={OrderId}, tamara_order_id={TamaraOrderId}, tamara_checkout_id={TamaraCheckoutId}", 
                        orderId, tamaraOrderId, tamaraCheckoutId);
                }

                return new OkObjectResult(new { success = true, message = "Tamara session created successfully", checkout_id = tamaraCheckoutId, order_id = tamaraOrderId });
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

    private int? FetchOrderCheckoutDetails(int orderId)
    {
        var query = @"
            SELECT oc.OrderCheckOutID
            FROM Orders o
            JOIN OrderCheckout oc ON oc.OrderID = o.OrderID
            WHERE o.OrderID = @OrderID";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@OrderID", orderId);
            
            var result = command.ExecuteScalar();
            return result as int?;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order checkout details for OrderID: {OrderId}", orderId);
            return null;
        }
    }

    private void UpdateOrderCheckoutDetailsUsingTamara(string? orderId, int orderStatus)
    {
        if (string.IsNullOrEmpty(orderId))
        {
            _logger.LogWarning("OrderID is null or empty, cannot update order status");
            return;
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (orderStatus == 106) // order_refunded
                {
                    // Update OrderCheckoutDetails
                    var query1 = @"
                        UPDATE OrderCheckoutDetails
                        SET OrderStatus = @OrderStatus
                        WHERE OrderCheckOutID = (
                            SELECT OrderCheckOutID        
                            FROM TamaraOrders
                            WHERE TamaraOrderID = @TamaraOrderID
                        )
                        AND CardType = 'Tamara'";

                    using var command1 = new SqlCommand(query1, connection, transaction);
                    command1.Parameters.AddWithValue("@OrderStatus", orderStatus);
                    command1.Parameters.AddWithValue("@TamaraOrderID", orderId);
                    var rowsAffected1 = command1.ExecuteNonQuery();

                    if (rowsAffected1 == 0)
                    {
                        _logger.LogWarning("No OrderCheckoutDetails updated for TamaraOrderID={TamaraOrderId}", orderId);
                    }

                    // Update OrderCheckout
                    var query2 = @"
                        UPDATE OrderCheckout
                        SET OrderStatus = @OrderStatus
                        WHERE OrderCheckOutID = (
                            SELECT OrderCheckOutID        
                            FROM TamaraOrders
                            WHERE TamaraOrderID = @TamaraOrderID
                        )";

                    using var command2 = new SqlCommand(query2, connection, transaction);
                    command2.Parameters.AddWithValue("@OrderStatus", orderStatus);
                    command2.Parameters.AddWithValue("@TamaraOrderID", orderId);
                    command2.ExecuteNonQuery();

                    // Update Orders - First, fetch the OrderID
                    var fetchOrderIdQuery = @"
                        SELECT OrderID
                        FROM OrderCheckout
                        WHERE OrderCheckOutID = (
                            SELECT OrderCheckOutID        
                            FROM TamaraOrders
                            WHERE TamaraOrderID = @TamaraOrderID
                        )";

                    using var fetchCommand = new SqlCommand(fetchOrderIdQuery, connection, transaction);
                    fetchCommand.Parameters.AddWithValue("@TamaraOrderID", orderId);
                    var realOrderId = fetchCommand.ExecuteScalar() as int?;

                    if (realOrderId.HasValue)
                    {
                        _logger.LogInformation("Real OrderID for TamaraOrderID={TamaraOrderId} is {RealOrderId}", orderId, realOrderId.Value);
                        
                        var query3 = @"
                            UPDATE Orders
                            SET StatusID = @OrderStatus
                            WHERE OrderID = @OrderID";

                        using var command3 = new SqlCommand(query3, connection, transaction);
                        command3.Parameters.AddWithValue("@OrderStatus", orderStatus);
                        command3.Parameters.AddWithValue("@OrderID", realOrderId.Value);
                        command3.ExecuteNonQuery();
                    }
                    else
                    {
                        _logger.LogWarning("No OrderID found for TamaraOrderID={TamaraOrderId} when updating Orders table", orderId);
                    }

                    transaction.Commit();
                    _logger.LogInformation("OrderCheckoutDetails, OrderCheckout, and Orders updated for TamaraOrderID={TamaraOrderId} to status {OrderStatus}", orderId, orderStatus);
                }
                else
                {
                    // For other statuses, only update OrderCheckoutDetails
                    var query = @"
                        UPDATE OrderCheckoutDetails
                        SET OrderStatus = @OrderStatus
                        WHERE OrderCheckOutID = (
                            SELECT OrderCheckOutID        
                            FROM TamaraOrders
                            WHERE TamaraOrderID = @TamaraOrderID
                        )
                        AND CardType = 'Tamara'";

                    using var command = new SqlCommand(query, connection, transaction);
                    command.Parameters.AddWithValue("@OrderStatus", orderStatus);
                    command.Parameters.AddWithValue("@TamaraOrderID", orderId);
                    var rowsAffected = command.ExecuteNonQuery();

                    if (rowsAffected == 0)
                    {
                        _logger.LogWarning("No OrderCheckoutDetails updated for TamaraOrderID={TamaraOrderId}", orderId);
                    }
                    else
                    {
                        transaction.Commit();
                        _logger.LogInformation("OrderCheckoutDetails updated for TamaraOrderID={TamaraOrderId} to status {OrderStatus}", orderId, orderStatus);
                    }
                }
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update OrderCheckoutDetails for TamaraOrderID={TamaraOrderId}", orderId);
            throw;
        }
    }
}