using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Karage.Functions.Models;
using Karage.Functions.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Karage.Functions.Functions;

public class MoyasarFunctions
{
    private readonly ILogger<MoyasarFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IConfiguration _configuration;

    public MoyasarFunctions(
        ILogger<MoyasarFunctions> logger,
        V1DbContext context,
        IConfiguration configuration)
    {
        _logger = logger;
        _context = context;
        _configuration = configuration;
    }

    [Function("MoyasarWebhook")]
    [OpenApiOperation(operationId: "MoyasarWebhook", tags: new[] { "Moyasar Payment Gateway" }, Summary = "Receive Moyasar payment webhooks", Description = "Webhook endpoint for Moyasar payment notifications. Validates x-event-secret and stores payment data.")]
    [OpenApiSecurity("x-event-secret", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-event-secret", Description = "Moyasar webhook secret for verification")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MoyasarWebhookRequest), Required = true, Description = "Moyasar webhook payload")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(WebhookResponse), Description = "Webhook processed successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid webhook signature")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(MoyasarErrorResponse), Description = "Invalid webhook payload")]
    public async Task<IActionResult> MoyasarWebhook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            // Read the request body
            string requestBody;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("Received empty webhook payload");
                return new BadRequestObjectResult(new MoyasarErrorResponse
                {
                    Status = 400,
                    Message = "Empty webhook payload"
                });
            }

            // Verify x-event-secret header
            if (!req.Headers.TryGetValue("x-event-secret", out var receivedSecret))
            {
                _logger.LogWarning("Missing x-event-secret header");
                return new UnauthorizedObjectResult(new MoyasarErrorResponse
                {
                    Status = 401,
                    Message = "Missing x-event-secret header"
                });
            }

            var configuredSecret = _configuration["MOYASAR_WEBHOOK_SECRET"];
            if (string.IsNullOrEmpty(configuredSecret))
            {
                _logger.LogError("Moyasar:WebhookSecret not configured");
                return new StatusCodeResult(500);
            }

            if (receivedSecret != configuredSecret)
            {
                _logger.LogWarning("Invalid x-event-secret received");
                return new UnauthorizedObjectResult(new MoyasarErrorResponse
                {
                    Status = 401,
                    Message = "Invalid webhook secret"
                });
            }

            // Parse the webhook payload
            MoyasarWebhookRequest? webhookRequest;
            try
            {
                webhookRequest = JsonSerializer.Deserialize<MoyasarWebhookRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse webhook payload");
                return new BadRequestObjectResult(new MoyasarErrorResponse
                {
                    Status = 400,
                    Message = "Invalid JSON payload"
                });
            }

            if (webhookRequest?.Data == null)
            {
                _logger.LogWarning("Webhook payload missing data field");
                return new BadRequestObjectResult(new MoyasarErrorResponse
                {
                    Status = 400,
                    Message = "Invalid webhook structure"
                });
            }

            var paymentData = webhookRequest.Data;

            // Extract metadata
            int? customerId = null;
            string? customerPhoneNumber = null;
            int? offerId = null;
            decimal? paymentValue = null;

            if (paymentData.Metadata != null)
            {
                if (paymentData.Metadata.TryGetValue("CustomerID", out var customerIdValue))
                {
                    if (int.TryParse(customerIdValue?.ToString(), out var parsedCustomerId))
                        customerId = parsedCustomerId;
                }

                if (paymentData.Metadata.TryGetValue("CustomerPhoneNumber", out var phoneValue))
                {
                    customerPhoneNumber = phoneValue?.ToString();
                }

                if (paymentData.Metadata.TryGetValue("OfferID", out var offerIdValue))
                {
                    if (int.TryParse(offerIdValue?.ToString(), out var parsedOfferId))
                        offerId = parsedOfferId;
                }

                if (paymentData.Metadata.TryGetValue("PaymentValue", out var paymentValueObj))
                {
                    if (decimal.TryParse(paymentValueObj?.ToString(), out var parsedPaymentValue))
                        paymentValue = parsedPaymentValue;
                }
            }

            // Check if payment already exists
            var existingWebhook = await _context.MoyasarPaymentWebhooks
                .FirstOrDefaultAsync(w => w.PaymentId == paymentData.Id);

            if (existingWebhook != null)
            {
                // Update existing webhook
                existingWebhook.Status = paymentData.Status;
                existingWebhook.Amount = paymentData.Amount;
                existingWebhook.Currency = paymentData.Currency;
                existingWebhook.PaymentMethod = paymentData.Source?.Type;
                existingWebhook.CustomerID = customerId;
                existingWebhook.CustomerPhoneNumber = customerPhoneNumber;
                existingWebhook.OfferID = offerId;
                existingWebhook.PaymentValue = paymentValue;
                existingWebhook.WebhookPayload = requestBody;
                existingWebhook.EventType = webhookRequest.Type;
                existingWebhook.IsVerified = true;
                existingWebhook.UpdatedAt = DateTime.UtcNow;

                _logger.LogInformation("Updating existing webhook for payment {PaymentId}", paymentData.Id);
            }
            else
            {
                // Create new webhook record
                var webhook = new MoyasarPaymentWebhook
                {
                    PaymentId = paymentData.Id,
                    Status = paymentData.Status,
                    Amount = paymentData.Amount,
                    Currency = paymentData.Currency,
                    PaymentMethod = paymentData.Source?.Type,
                    CustomerID = customerId,
                    CustomerPhoneNumber = customerPhoneNumber,
                    OfferID = offerId,
                    PaymentValue = paymentValue,
                    WebhookPayload = requestBody,
                    EventType = webhookRequest.Type,
                    IsVerified = true,
                    IsProcessed = false,
                    CreatedAt = DateTime.UtcNow
                };

                _context.MoyasarPaymentWebhooks.Add(webhook);
                _logger.LogInformation("Created new webhook record for payment {PaymentId}", paymentData.Id);
            }

            await _context.SaveChangesAsync();

            return new OkObjectResult(new WebhookResponse
            {
                Status = 200,
                Message = "Webhook processed successfully",
                PaymentId = paymentData.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Moyasar webhook");
            return new StatusCodeResult(500);
        }
    }
}

public class WebhookResponse
{
    public int Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
}

public class MoyasarErrorResponse
{
    public int Status { get; set; }
    public string Message { get; set; } = string.Empty;
}
