using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using Microsoft.Extensions.Configuration;

namespace Karage.Functions.Functions;

public class OrderFunctions
{
    private readonly ILogger<OrderFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IApiKeyService _apiKeyService;

    public OrderFunctions(ILogger<OrderFunctions> logger, V1DbContext context, IConfiguration configuration, IApiKeyService apiKeyService)
    {
        _logger = logger;
        _context = context;
        _configuration = configuration;
        _apiKeyService = apiKeyService;
    }

    [Function("GetOrderPayload")]
    public async Task<IActionResult> GetOrderPayload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Get order payload endpoint called.");

            var (verificationResult, user) = await _apiKeyService.VerifyApiKeyAndGetUser(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            if (user == null)
            {
                return new UnauthorizedResult();
            }
            var customerIdStr = req.Query["customerId"].ToString();
            if (string.IsNullOrEmpty(customerIdStr) || !int.TryParse(customerIdStr, out int customerId))
            {
                return new BadRequestObjectResult(new { error = "Valid customer ID is required" });
            }

try{
            var order = await _context.Orders
                .Where(o => o.CustomerID == customerId)
                .OrderByDescending(o => o.OrderCreatedDT)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return new NotFoundObjectResult(new { message = "No orders found for the provided customer ID" });
            }

            // Get order details based on the order ID
            var orderDetails = await _context.OrderDetails
                .Where(od => od.OrderID == order.OrderID)
                .Include(od => od.Item)
                .Include(od => od.Package)
                .ToListAsync();

            var response = new
            {
                Order = order,
                OrderDetails = orderDetails
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving order payload for customer ID: {CustomerId}", customerIdStr);
            return new StatusCodeResult(500);
       }
   }
}
   
