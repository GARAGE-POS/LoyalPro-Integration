using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using System.Net;
using System.Text.Json;

namespace Karage.Functions.Functions;

public class BoukakFunctions
{
    private readonly ILogger<BoukakFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly ISessionAuthService _sessionAuthService;
    private readonly IBoukakApiService _boukakApiService;

    // Default template ID - should be configurable per location
    private const string DefaultTemplateId = "default-template-id";

    public BoukakFunctions(
        ILogger<BoukakFunctions> logger,
        V1DbContext context,
        ISessionAuthService sessionAuthService,
        IBoukakApiService boukakApiService)
    {
        _logger = logger;
        _context = context;
        _sessionAuthService = sessionAuthService;
        _boukakApiService = boukakApiService;
    }

    [Function("CreateBoukakCustomerCard")]
    [OpenApiOperation(operationId: "CreateBoukakCustomerCard", tags: new[] { "Boukak Integration" },
        Summary = "Create Boukak customer loyalty card",
        Description = "Creates a new customer loyalty card in Boukak system when a customer is created locally")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateBoukakCustomerCardRequest),
        Description = "Customer information for card creation")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(BoukakCustomerCardResponse), Description = "Customer card created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
        bodyType: typeof(ErrorResponse), Description = "Bad request or creation error")]
    public async Task<IActionResult> CreateBoukakCustomerCard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Create Boukak customer card endpoint called.");

        try
        {
            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var createRequest = JsonSerializer.Deserialize<CreateBoukakCustomerCardRequest>(requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (createRequest == null || createRequest.CustomerId <= 0)
            {
                return new BadRequestObjectResult(new { error = "Invalid request. CustomerId is required." });
            }

            _logger.LogInformation("Processing request for Customer: {CustomerId}",
                createRequest.CustomerId);

            // Get customer from local database
            var customer = await _context.Customers
                .Where(c => c.CustomerID == createRequest.CustomerId && c.StatusID == 1)
                .FirstOrDefaultAsync();

            if (customer == null)
            {
                return new BadRequestObjectResult(new { error = $"Customer with ID {createRequest.CustomerId} not found or inactive" });
            }

            // Use customer's LocationID for mapping context (defaults to 0 if null)
            var locationId = customer.LocationID ?? 0;

            // Check if customer already has a Boukak card
            var existingMapping = await _context.BoukakCustomerMappings
                .FirstOrDefaultAsync(m => m.CustomerId == createRequest.CustomerId && m.LocationId == locationId);

            if (existingMapping != null)
            {
                _logger.LogInformation("Customer {CustomerId} already has Boukak card: {CardId}",
                    createRequest.CustomerId, existingMapping.BoukakCardId);

                return new OkObjectResult(new
                {
                    message = "Customer already has a Boukak card",
                    customerId = customer.CustomerID,
                    boukakCustomerId = existingMapping.BoukakCustomerId,
                    boukakCardId = existingMapping.BoukakCardId,
                    status = "existing"
                });
            }

            // Prepare Boukak API request
            var boukakRequest = new BoukakCustomerCardRequest
            {
                templateId = createRequest.TemplateId ?? DefaultTemplateId,
                platform = createRequest.Platform ?? "android",
                language = createRequest.Language ?? "en",
                customerData = new BoukakCustomerData
                {
                    firstname = customer.FullName?.Split(' ').FirstOrDefault() ?? customer.UserName,
                    lastname = customer.FullName?.Split(' ').Skip(1).FirstOrDefault() ?? "",
                    phone = customer.Mobile,
                    email = customer.Email,
                    dob = customer.DOB,
                    gender = customer.Sex,
                    initialCashback = createRequest.InitialCashback ?? 0
                }
            };

            _logger.LogInformation("Creating Boukak card for customer {CustomerId} ({CustomerName})",
                customer.CustomerID, customer.FullName);

            // Call Boukak API
            var boukakResponse = await _boukakApiService.CreateCustomerCardAsync(boukakRequest);

            if (boukakResponse == null || !boukakResponse.success)
            {
                return new BadRequestObjectResult(new
                {
                    error = "Failed to create Boukak customer card",
                    message = boukakResponse?.message
                });
            }

            // Validate that we received customer ID and card ID
            if (string.IsNullOrEmpty(boukakResponse.customerId) || string.IsNullOrEmpty(boukakResponse.cardId))
            {
                _logger.LogWarning("Boukak API did not return customerId or cardId in headers");
                return new BadRequestObjectResult(new
                {
                    error = "Boukak API response missing customer or card ID"
                });
            }

            // Create mapping in local database
            var mapping = new BoukakCustomerMapping
            {
                CustomerId = customer.CustomerID,
                BoukakCustomerId = boukakResponse.customerId,
                BoukakCardId = boukakResponse.cardId,
                LocationId = locationId,
                CreatedAt = DateTime.UtcNow
            };

            _context.BoukakCustomerMappings.Add(mapping);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully created Boukak card for customer {CustomerId}. Boukak CardId: {CardId}",
                customer.CustomerID, boukakResponse.cardId);

            return new OkObjectResult(new
            {
                message = "Boukak customer card created successfully",
                customerId = customer.CustomerID,
                boukakCustomerId = boukakResponse.customerId,
                boukakCardId = boukakResponse.cardId,
                applePassUrl = boukakResponse.applePassUrl,
                passWalletUrl = boukakResponse.passWalletUrl,
                status = "created"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateBoukakCustomerCard function");
            return new StatusCodeResult(500);
        }
    }

    [Function("AddBoukakStamps")]
    [OpenApiOperation(operationId: "AddBoukakStamps", tags: new[] { "Boukak Integration" },
        Summary = "Add stamps to Boukak customer card",
        Description = "Adds loyalty stamps to a customer's Boukak card when they complete an order")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AddBoukakStampsRequest),
        Description = "Order information for stamp addition")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(BoukakAddStampsResponse), Description = "Stamps added successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
        bodyType: typeof(ErrorResponse), Description = "Bad request or stamp addition error")]
    public async Task<IActionResult> AddBoukakStamps(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Add Boukak stamps endpoint called.");

        try
        {
            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var stampRequest = JsonSerializer.Deserialize<AddBoukakStampsRequest>(requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (stampRequest == null || stampRequest.OrderId <= 0)
            {
                return new BadRequestObjectResult(new { error = "Invalid request. OrderId is required." });
            }

            _logger.LogInformation("Processing stamp request for Order: {OrderId}",
                stampRequest.OrderId);

            // Get order with customer information
            var order = await _context.Orders
                .Include(o => o.Customer)
                .Include(o => o.OrderCheckout)
                .Where(o => o.OrderID == stampRequest.OrderId)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return new BadRequestObjectResult(new { error = $"Order with ID {stampRequest.OrderId} not found" });
            }

            // Use order's LocationID for mapping context
            var locationId = order.LocationID;

            if (order.CustomerID == null || order.Customer == null)
            {
                return new BadRequestObjectResult(new { error = "Order does not have an associated customer" });
            }

            // Validate order status - only add stamps for completed orders
            if (order.StatusID != 600) // Assuming 600 is completed status
            {
                return new BadRequestObjectResult(new
                {
                    error = "Can only add stamps to completed orders",
                    currentStatus = order.StatusID
                });
            }

            // Check if customer has a Boukak card
            var customerMapping = await _context.BoukakCustomerMappings
                .FirstOrDefaultAsync(m => m.CustomerId == order.CustomerID && m.LocationId == locationId);

            if (customerMapping == null)
            {
                return new BadRequestObjectResult(new
                {
                    error = "Customer does not have a Boukak loyalty card",
                    customerId = order.CustomerID
                });
            }

            // Calculate stamps to add (default to 1 stamp per order, can be customized)
            int stampsToAdd = stampRequest.Stamps ?? 1;

            // Prepare product information (optional)
            BoukakProductInfo? productInfo = null;
            if (order.OrderCheckout != null)
            {
                productInfo = new BoukakProductInfo
                {
                    name = $"Order #{order.OrderID}",
                    price = (decimal?)order.OrderCheckout.GrandTotal ?? 0
                };
            }

            // Call Boukak API to add stamps
            var boukakStampRequest = new BoukakAddStampsRequest
            {
                cardId = customerMapping.BoukakCardId,
                stamps = stampsToAdd,
                products = productInfo
            };

            _logger.LogInformation("Adding {Stamps} stamps to Boukak card {CardId} for order {OrderId}",
                stampsToAdd, customerMapping.BoukakCardId, order.OrderID);

            var boukakResponse = await _boukakApiService.AddStampsAsync(boukakStampRequest);

            if (boukakResponse == null || !boukakResponse.success)
            {
                return new BadRequestObjectResult(new
                {
                    error = "Failed to add stamps to Boukak card",
                    message = boukakResponse?.message
                });
            }

            _logger.LogInformation("Successfully added {Stamps} stamps to Boukak card. Active stamps: {ActiveStamps}, Rewards: {Rewards}",
                stampsToAdd, boukakResponse.activeStamps, boukakResponse.rewards);

            return new OkObjectResult(new
            {
                message = "Stamps added successfully",
                orderId = order.OrderID,
                customerId = order.CustomerID,
                boukakCardId = customerMapping.BoukakCardId,
                stampsAdded = stampsToAdd,
                activeStamps = boukakResponse.activeStamps,
                rewards = boukakResponse.rewards
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AddBoukakStamps function");
            return new StatusCodeResult(500);
        }
    }

    [Function("BoukakWebhook")]
    [OpenApiOperation(operationId: "BoukakWebhook", tags: new[] { "Boukak Integration" },
        Summary = "Boukak webhook handler",
        Description = "Receives webhook events from Boukak (CARD_INSTALLED, CARD_UNINSTALLED)")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(BoukakWebhookPayload),
        Description = "Webhook payload from Boukak")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.OK, Description = "Webhook processed successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json",
        bodyType: typeof(ErrorResponse), Description = "Invalid webhook payload")]
    public async Task<IActionResult> BoukakWebhook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Boukak webhook endpoint called.");

        try
        {
            // Read webhook payload
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("Received Boukak webhook payload: {Payload}", requestBody);

            var webhookPayload = JsonSerializer.Deserialize<BoukakWebhookPayload>(requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (webhookPayload == null || string.IsNullOrEmpty(webhookPayload.@event))
            {
                _logger.LogWarning("Invalid webhook payload received");
                return new BadRequestObjectResult(new { error = "Invalid webhook payload" });
            }

            _logger.LogInformation("Processing Boukak webhook event: {Event}, CardId: {CardId}, CustomerId: {CustomerId}",
                webhookPayload.@event,
                webhookPayload.data?.cardId,
                webhookPayload.data?.customerId);

            // Handle different webhook events
            switch (webhookPayload.@event)
            {
                case "CARD_INSTALLED":
                    await HandleCardInstalled(webhookPayload);
                    break;

                case "CARD_UNINSTALLED":
                    await HandleCardUninstalled(webhookPayload);
                    break;

                default:
                    _logger.LogWarning("Unknown webhook event type: {Event}", webhookPayload.@event);
                    break;
            }

            // Always return 200 OK to acknowledge receipt
            return new OkObjectResult(new { message = "Webhook received successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Boukak webhook");
            // Still return 200 to prevent retries for unrecoverable errors
            return new OkObjectResult(new { message = "Webhook received with errors - check logs" });
        }
    }

    private async Task HandleCardInstalled(BoukakWebhookPayload payload)
    {
        _logger.LogInformation("Handling CARD_INSTALLED event for CardId: {CardId}", payload.data?.cardId);

        if (string.IsNullOrEmpty(payload.data?.cardId))
        {
            _logger.LogWarning("CARD_INSTALLED event missing cardId");
            return;
        }

        // Find customer mapping
        var mapping = await _context.BoukakCustomerMappings
            .FirstOrDefaultAsync(m => m.BoukakCardId == payload.data.cardId);

        if (mapping != null)
        {
            _logger.LogInformation("Customer {CustomerId} installed Boukak card {CardId}",
                mapping.CustomerId, payload.data.cardId);

            // TODO: Add business logic here (e.g., send welcome message, award bonus points, etc.)
        }
        else
        {
            _logger.LogWarning("No customer mapping found for Boukak card {CardId}", payload.data.cardId);
        }
    }

    private async Task HandleCardUninstalled(BoukakWebhookPayload payload)
    {
        _logger.LogInformation("Handling CARD_UNINSTALLED event for CardId: {CardId}", payload.data?.cardId);

        if (string.IsNullOrEmpty(payload.data?.cardId))
        {
            _logger.LogWarning("CARD_UNINSTALLED event missing cardId");
            return;
        }

        // Find customer mapping
        var mapping = await _context.BoukakCustomerMappings
            .FirstOrDefaultAsync(m => m.BoukakCardId == payload.data.cardId);

        if (mapping != null)
        {
            _logger.LogInformation("Customer {CustomerId} uninstalled Boukak card {CardId}",
                mapping.CustomerId, payload.data.cardId);

            // TODO: Add business logic here (e.g., send feedback request, update customer preferences, etc.)
        }
        else
        {
            _logger.LogWarning("No customer mapping found for Boukak card {CardId}", payload.data.cardId);
        }
    }

    [Function("SyncFirst10CustomersToBoukak")]
    [OpenApiOperation(operationId: "SyncFirst10CustomersToBoukak", tags: new[] { "Boukak Integration" },
        Summary = "Sync first 10 customers to Boukak",
        Description = "Creates Boukak loyalty cards for the first 10 active customers")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Sync results")]
    public async Task<IActionResult> SyncFirst10CustomersToBoukak(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Sync first 10 customers to Boukak endpoint called.");

        var results = new List<object>();
        var templateId = "0p7KrSlSVGdsmRlqV50z";
        var platform = "ios";
        var language = "ar";

        try
        {
            // Get first 10 active customers
            var customers = await _context.Customers
                .Where(c => c.StatusID == 1)
                .OrderBy(c => c.CustomerID)
                .Take(10)
                .ToListAsync();

            _logger.LogInformation("Found {Count} customers to sync", customers.Count);

            foreach (var customer in customers)
            {
                try
                {
                    var locationId = customer.LocationID ?? 0;

                    // Check if customer already has a Boukak card
                    var existingMapping = await _context.BoukakCustomerMappings
                        .FirstOrDefaultAsync(m => m.CustomerId == customer.CustomerID && m.LocationId == locationId);

                    if (existingMapping != null)
                    {
                        _logger.LogInformation("Customer {CustomerId} ({Name}) already has Boukak card: {CardId}",
                            customer.CustomerID, customer.FullName, existingMapping.BoukakCardId);

                        results.Add(new
                        {
                            customerId = customer.CustomerID,
                            customerName = customer.FullName,
                            status = "existing",
                            boukakCardId = existingMapping.BoukakCardId,
                            message = "Customer already has a Boukak card"
                        });
                        continue;
                    }

                    // Prepare Boukak API request
                    var boukakRequest = new BoukakCustomerCardRequest
                    {
                        templateId = templateId,
                        platform = platform,
                        language = language,
                        customerData = new BoukakCustomerData
                        {
                            firstname = customer.FullName?.Split(' ').FirstOrDefault() ?? customer.UserName,
                            lastname = customer.FullName?.Split(' ').Skip(1).FirstOrDefault() ?? "",
                            phone = customer.Mobile,
                            email = customer.Email,
                            dob = customer.DOB,
                            gender = customer.Sex,
                            initialCashback = 0
                        }
                    };

                    _logger.LogInformation("Creating Boukak card for customer {CustomerId} ({CustomerName})",
                        customer.CustomerID, customer.FullName);

                    // Call Boukak API
                    var boukakResponse = await _boukakApiService.CreateCustomerCardAsync(boukakRequest);

                    if (boukakResponse == null || !boukakResponse.success)
                    {
                        _logger.LogWarning("Failed to create Boukak card for customer {CustomerId}: {Message}",
                            customer.CustomerID, boukakResponse?.message);

                        results.Add(new
                        {
                            customerId = customer.CustomerID,
                            customerName = customer.FullName,
                            status = "failed",
                            error = boukakResponse?.message ?? "Unknown error"
                        });
                        continue;
                    }

                    // Validate response
                    if (string.IsNullOrEmpty(boukakResponse.customerId) || string.IsNullOrEmpty(boukakResponse.cardId))
                    {
                        _logger.LogWarning("Boukak API did not return customerId or cardId for customer {CustomerId}",
                            customer.CustomerID);

                        results.Add(new
                        {
                            customerId = customer.CustomerID,
                            customerName = customer.FullName,
                            status = "failed",
                            error = "Boukak API response missing customer or card ID"
                        });
                        continue;
                    }

                    // Create mapping in local database
                    var mapping = new BoukakCustomerMapping
                    {
                        CustomerId = customer.CustomerID,
                        BoukakCustomerId = boukakResponse.customerId,
                        BoukakCardId = boukakResponse.cardId,
                        LocationId = locationId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.BoukakCustomerMappings.Add(mapping);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Successfully created Boukak card for customer {CustomerId}. Boukak CardId: {CardId}",
                        customer.CustomerID, boukakResponse.cardId);

                    results.Add(new
                    {
                        customerId = customer.CustomerID,
                        customerName = customer.FullName,
                        status = "created",
                        boukakCustomerId = boukakResponse.customerId,
                        boukakCardId = boukakResponse.cardId,
                        applePassUrl = boukakResponse.applePassUrl,
                        passWalletUrl = boukakResponse.passWalletUrl
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing customer {CustomerId}", customer.CustomerID);
                    results.Add(new
                    {
                        customerId = customer.CustomerID,
                        customerName = customer.FullName,
                        status = "error",
                        error = ex.Message
                    });
                }
            }

            // Write results to file
            var resultsJson = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
            var resultsFilePath = "/tmp/boukak_sync_results.json";
            await System.IO.File.WriteAllTextAsync(resultsFilePath, resultsJson);

            _logger.LogInformation("Sync completed. Results saved to {FilePath}", resultsFilePath);

            return new OkObjectResult(new
            {
                message = "Sync completed",
                totalCustomers = customers.Count,
                resultsFilePath = resultsFilePath,
                summary = new
                {
                    created = results.Count(r => ((dynamic)r).status == "created"),
                    existing = results.Count(r => ((dynamic)r).status == "existing"),
                    failed = results.Count(r => ((dynamic)r).status == "failed"),
                    errors = results.Count(r => ((dynamic)r).status == "error")
                },
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncFirst10CustomersToBoukak function");
            return new StatusCodeResult(500);
        }
    }
}

// Request models for OpenAPI documentation
public class CreateBoukakCustomerCardRequest
{
    public int CustomerId { get; set; }
    public string? TemplateId { get; set; }
    public string? Platform { get; set; } // "android" or "iOS"
    public string? Language { get; set; } // "en" or "ar"
    public decimal? InitialCashback { get; set; }
}

public class AddBoukakStampsRequest
{
    public int OrderId { get; set; }
    public int? Stamps { get; set; } // Number of stamps to add (default 1)
}

// Response model for OpenAPI documentation
public class BoukakCustomerCardResponse
{
    public string message { get; set; } = string.Empty;
    public int customerId { get; set; }
    public string boukakCustomerId { get; set; } = string.Empty;
    public string boukakCardId { get; set; } = string.Empty;
    public string? applePassUrl { get; set; }
    public string? passWalletUrl { get; set; }
    public string status { get; set; } = string.Empty;
}
