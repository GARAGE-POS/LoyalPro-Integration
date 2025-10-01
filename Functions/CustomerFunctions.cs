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
using System.Text.RegularExpressions;


namespace Karage.Functions.Functions;

public class CustomerFunctions
{
    private readonly ILogger<CustomerFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IApiKeyService _apiKeyService;

    public CustomerFunctions(ILogger<CustomerFunctions> logger, V1DbContext context, IApiKeyService apiKeyService)
    {
        _logger = logger;
        _context = context;
        _apiKeyService = apiKeyService;
    }


    [Function("Customers")]
    [OpenApiOperation(operationId: "SearchCustomerByPhoneNumber", tags: new[] { "Customers" }, Summary = "Search customer by phone number", Description = "Searches for a customer using their phone number")]
    [OpenApiSecurity("X-API-Key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
    [OpenApiParameter(name: "filter[phone]", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Customer phone number to search for")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Customer found successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Customer not found")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid phone number format")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing API key")]
    public async Task<IActionResult> SearchCustomerByPhoneNumber(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Search customer by phone number endpoint called.");

        try
        {
            var verificationResult = await _apiKeyService.VerifyApiKey(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            var phoneNumber = req.Query["filter[phone]"].ToString();

            if (string.IsNullOrEmpty(phoneNumber))
            {
                return new BadRequestObjectResult(new { error = "Phone number is required" });
            }

            var normalizedInput = NormalizePhone(phoneNumber);
            if (normalizedInput == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid phone number format" });
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.StatusID == 1 && c.Mobile == normalizedInput);

            if (customer == null)
            {
                return new NotFoundObjectResult(new { message = "No customer found with the provided phone number" });
            }

            var formattedCustomer = new
            {
                customerId = customer.CustomerID,
                name = customer.FullName,
                email = customer.Email,
                phone = customer.Mobile.StartsWith("+966") ? customer.Mobile.Substring(4) : customer.Mobile,
                phone_prefix = 966
            };

            return new OkObjectResult(formattedCustomer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching customer by phone number");
            return new StatusCodeResult(500);
        }
    }

    [Function("CreateCustomer")]
    [OpenApiOperation(operationId: "CreateCustomer", tags: new[] { "Customers" }, Summary = "Create new customer", Description = "Creates a new customer with the provided information")]
    [OpenApiSecurity("X-API-Key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateCustomerRequest), Required = true, Description = "Customer information")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Customer created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request data")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Customer with phone number already exists")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing API key")]
    public async Task<IActionResult> AddCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Add customer endpoint called.");

        try
        {
            var verificationResult = await _apiKeyService.VerifyApiKey(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(requestBody);
            var root = doc.RootElement;

            var customer = new Customer();

            // Map payload fields
            if (root.TryGetProperty("name", out var nameElement))
            {
                customer.FullName = nameElement.GetString();
            }
            if (root.TryGetProperty("email", out var emailElement))
            {
                customer.Email = emailElement.GetString();
            }

            string? phone = null;
            int? phonePrefix = null;
            if (root.TryGetProperty("phone", out var phoneElement))
            {
                phone = phoneElement.GetString();
            }
            if (root.TryGetProperty("phone_prefix", out var prefixElement))
            {
                phonePrefix = prefixElement.GetInt32();
            }

            if (string.IsNullOrEmpty(phone) || !phonePrefix.HasValue)
            {
                return new BadRequestObjectResult(new { error = "Phone and phone_prefix are required" });
            }

            var combinedPhone = $"+{phonePrefix}{phone}";
            var normalizedMobile = NormalizePhone(combinedPhone);
            if (normalizedMobile == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid phone number format" });
            }
            customer.Mobile = normalizedMobile;

            // Check if customer already exists with same mobile number
            var existingCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Mobile == customer.Mobile);

            if (existingCustomer != null)
            {
                return new ConflictObjectResult(new { error = "Customer with this mobile number already exists" });
            }

            // Set default values
            customer.CreatedOn = DateTime.UtcNow;
            customer.StatusID = 1;
            customer.RowID = 0;

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            var formattedCustomer = new
            {
                customerId = customer.CustomerID,
                name = customer.FullName,
                email = customer.Email,
                phone = customer.Mobile.StartsWith("+966") ? customer.Mobile.Substring(4) : customer.Mobile,
                phone_prefix = 966
            };

            return new CreatedResult($"/api/customers/{customer.CustomerID}", formattedCustomer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding customer");
            return new StatusCodeResult(500);
        }
    }

    [Function("UpdateCustomer")]
    [OpenApiOperation(operationId: "UpdateCustomer", tags: new[] { "Customers" }, Summary = "Update customer", Description = "Updates an existing customer's information")]
    [OpenApiSecurity("X-API-Key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
    [OpenApiParameter(name: "customerId", In = ParameterLocation.Path, Required = true, Type = typeof(int), Description = "Customer ID to update")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateCustomerRequest), Required = true, Description = "Updated customer information")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Customer updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Customer not found")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing API key")]
    public async Task<IActionResult> UpdateCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "UpdateCustomer/{customerId}")] HttpRequest req)
    {
        _logger.LogInformation("Update customer endpoint called.");

        try
        {
            var verificationResult = await _apiKeyService.VerifyApiKey(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            var customerIdStr = req.RouteValues["customerId"]?.ToString();

            if (string.IsNullOrEmpty(customerIdStr) || !int.TryParse(customerIdStr, out int customerId))
            {
                return new BadRequestObjectResult(new { error = "Valid customer ID is required" });
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(requestBody);
            var root = doc.RootElement;

            var existingCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.CustomerID == customerId);

            if (existingCustomer == null)
            {
                return new NotFoundObjectResult(new { error = "Customer not found" });
            }

            // Map payload fields
            if (root.TryGetProperty("name", out var nameElement))
            {
                existingCustomer.FullName = nameElement.GetString();
            }
            if (root.TryGetProperty("email", out var emailElement))
            {
                existingCustomer.Email = emailElement.GetString();
            }

            string? phone = null;
            int? phonePrefix = null;
            if (root.TryGetProperty("phone", out var phoneElement))
            {
                phone = phoneElement.GetString();
            }
            if (root.TryGetProperty("phone_prefix", out var prefixElement))
            {
                phonePrefix = prefixElement.GetInt32();
            }

            if (!string.IsNullOrEmpty(phone) && phonePrefix.HasValue)
            {
                var combinedPhone = $"+{phonePrefix}{phone}";
                var normalizedMobile = NormalizePhone(combinedPhone);
                if (normalizedMobile == null)
                {
                    return new BadRequestObjectResult(new { error = "Invalid phone number format" });
                }
                existingCustomer.Mobile = normalizedMobile;
            }

            existingCustomer.LastUpdatedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var formattedCustomer = new
            {
                customerId = existingCustomer.CustomerID,
                name = existingCustomer.FullName,
                email = existingCustomer.Email,
                phone = existingCustomer.Mobile.StartsWith("+966") ? existingCustomer.Mobile.Substring(4) : existingCustomer.Mobile,
                phone_prefix = 966
            };

            return new OkObjectResult(formattedCustomer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating customer");
            return new StatusCodeResult(500);
        }
    }

    private string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return null;
        var trimmed = phone.Trim();
        if (trimmed.StartsWith("+966")) return trimmed;
        if (trimmed.StartsWith("966")) return "+966" + trimmed.Substring(3);
        if (trimmed.StartsWith("00966")) return "+966" + trimmed.Substring(5);
        if (trimmed.StartsWith("0")) return "+966" + trimmed.Substring(1);
        if (Regex.IsMatch(trimmed, @"^\d+$"))
        {
            return "+966" + trimmed;
        }
        return null;
    }
}

// OpenAPI Models for Customer functions
public class CreateCustomerRequest
{
    public string name { get; set; } = string.Empty;
    public string? email { get; set; }
    public string phone { get; set; } = string.Empty;
    public int phone_prefix { get; set; }
}

public class UpdateCustomerRequest
{
    public string? name { get; set; }
    public string? email { get; set; }
    public string? phone { get; set; }
    public int? phone_prefix { get; set; }
}