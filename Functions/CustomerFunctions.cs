using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using System.Text.RegularExpressions;


namespace Karage.Functions.Functions;

public class CustomerFunctions
{
    private readonly ILogger<CustomerFunctions> _logger;
    private readonly V1DbContext _context;

    public CustomerFunctions(ILogger<CustomerFunctions> logger, V1DbContext context)
    {
        _logger = logger;
        _context = context;
    }


    [Function("Customers")]
    public async Task<IActionResult> SearchCustomerByPhoneNumber(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        _logger.LogInformation("Search customer by phone number endpoint called.");

        try
        {
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

            var customers = await _context.Customers
                .Where(c => c.StatusID == 1 && c.Mobile == normalizedInput)
                .ToListAsync();

            if (!customers.Any())
            {
                return new NotFoundObjectResult(new { message = "No customers found with the provided phone number" });
            }

            var formattedCustomers = customers.Select(c => new
            {
                customerId = c.CustomerID,
                name = c.FullName,
                email = c.Email,
                phone = c.Mobile.StartsWith("+966") ? c.Mobile.Substring(4) : c.Mobile,
                phone_prefix = 966
            });

            return new OkObjectResult(formattedCustomers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching customer by phone number");
            return new StatusCodeResult(500);
        }
    }

    [Function("CreateCustomer")]
    public async Task<IActionResult> AddCustomer(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("Add customer endpoint called.");

        try
        {
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
    public async Task<IActionResult> UpdateCustomer(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "UpdateCustomer/{customerId}")] HttpRequest req)
    {
        _logger.LogInformation("Update customer endpoint called.");

        try
        {
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