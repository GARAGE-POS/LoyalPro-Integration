using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;


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


    [Function("SearchCustomerByPhoneNumber")]
    public async Task<IActionResult> SearchCustomerByPhoneNumber(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        _logger.LogInformation("Search customer by phone number endpoint called.");

        try
        {
            var phoneNumber = req.Query["phoneNumber"].ToString();
            
            if (string.IsNullOrEmpty(phoneNumber))
            {
                return new BadRequestObjectResult(new { error = "Phone number is required" });
            }

            var normalizedInput = NormalizePhone(phoneNumber);
            if (normalizedInput == null)
            {
                return new BadRequestObjectResult(new { error = "Phone number must start with +966" });
            }
                                      
                var customers = await _context.Customers
                    .Where(c => c.StatusID == 1 && c.Mobile == normalizedInput)
                    .ToListAsync();

            if (!customers.Any())
            {
                return new NotFoundObjectResult(new { message = "No customers found with the provided phone number" });
            }

            return new OkObjectResult(customers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching customer by phone number");
            return new StatusCodeResult(500);
        }
    }

    [Function("AddCustomer")]
    public async Task<IActionResult> AddCustomer(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("Add customer endpoint called.");

        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var customer = System.Text.Json.JsonSerializer.Deserialize<Customer>(requestBody, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (customer == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid customer data" });
            }

            // Validate required fields
            if (string.IsNullOrEmpty(customer.Mobile))
            {
                return new BadRequestObjectResult(new { error = "Mobile number is required" });
            }

            var normalizedMobile = NormalizePhone(customer.Mobile);
            if (normalizedMobile == null)
            {
                return new BadRequestObjectResult(new { error = "Phone number must start with +966" });
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

            return new CreatedResult($"/api/customers/{customer.CustomerID}", customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding customer");
            return new StatusCodeResult(500);
        }
    }

    [Function("UpdateCustomer")]
    public async Task<IActionResult> UpdateCustomer(
        [HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequest req)
    {
        _logger.LogInformation("Update customer endpoint called.");

        try
        {
            var customerIdStr = req.Query["customerId"].ToString();
            
            if (string.IsNullOrEmpty(customerIdStr) || !int.TryParse(customerIdStr, out int customerId))
            {
                return new BadRequestObjectResult(new { error = "Valid customer ID is required" });
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var updatedCustomer = System.Text.Json.JsonSerializer.Deserialize<Customer>(requestBody, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updatedCustomer == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid customer data" });
            }

            var existingCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.CustomerID == customerId);

            if (existingCustomer == null)
            {
                return new NotFoundObjectResult(new { error = "Customer not found" });
            }

            // Update fields
            existingCustomer.UserName = updatedCustomer.UserName ?? existingCustomer.UserName;
            existingCustomer.FullName = updatedCustomer.FullName ?? existingCustomer.FullName;
            existingCustomer.Email = updatedCustomer.Email ?? existingCustomer.Email;
            existingCustomer.DOB = updatedCustomer.DOB ?? existingCustomer.DOB;
            existingCustomer.Sex = updatedCustomer.Sex ?? existingCustomer.Sex;
            var normalizedMobile = NormalizePhone(updatedCustomer.Mobile ?? existingCustomer.Mobile);
            if (normalizedMobile == null)
            {
                return new BadRequestObjectResult(new { error = "Phone number must start with +966" });
            }
            existingCustomer.Mobile = normalizedMobile;
            existingCustomer.City = updatedCustomer.City ?? existingCustomer.City;
            existingCustomer.Country = updatedCustomer.Country ?? existingCustomer.Country;
            existingCustomer.LastUpdatedDate = DateTime.UtcNow;
            existingCustomer.LastUpdatedBy = updatedCustomer.LastUpdatedBy;

            await _context.SaveChangesAsync();

            return new OkObjectResult(existingCustomer);
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
        return null;
    }
}