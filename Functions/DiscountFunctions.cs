using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using System.Threading.Tasks;

namespace Karage.Functions.Functions;

public class DiscountFunctions
{
    private readonly ILogger<DiscountFunctions> _logger;
    private readonly V1DbContext _context;

    public DiscountFunctions(ILogger<DiscountFunctions> logger, V1DbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Function("GetDiscounts")]
    public async Task<IActionResult> GetDiscounts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Get discounts endpoint called.");

        try
        {
            // Verify Bearer token
            string? authHeader = req.Headers["Authorization"].FirstOrDefault();
            if (!TokenVerificationService.VerifyToken(authHeader ?? string.Empty, out string? customerId))
            {
                _logger.LogWarning("Invalid or missing authorization token");
                return new UnauthorizedObjectResult(new { error = "Invalid or missing authorization token" });
            }

            _logger.LogInformation("Token verified successfully for customer: {CustomerId}", customerId);

            // Get LocationID parameter from query string
            if (!req.Query.TryGetValue("LocationID", out var locationIdValue) ||
                !int.TryParse(locationIdValue, out int locationId))
            {
                return new BadRequestObjectResult(new { error = "LocationID parameter is required and must be a valid integer" });
            }

            _logger.LogInformation("Filtering discounts for location: {LocationId}", locationId);

            // Get discounts filtered by location and active status
            var discounts = await _context.Discounts
                .Where(d => d.StatusID == 1 && d.LocationID == locationId) // Active discounts for specific location
                .Select(d => new
                {
                    d.DiscountID,
                    d.Name,
                    d.DiscountType,
                    d.Value,
                    d.FromDate,
                    d.ToDate,
                    d.FromTime,
                    d.ToTime,
                    d.LocationID,
                    d.LastUpdatedDate,
                    d.LastUpdatedBy,
                    d.StatusID,
                    d.DiscountBy,
                    d.IsCouponCode,
                    d.Code,
                    d.NoOfRedemption
                })
                .ToListAsync();

            return new OkObjectResult(new
            {
                message = "Discounts retrieved successfully",
                customer_id = customerId,
                LocationID = locationId,
                total_discounts = discounts.Count,
                discounts = discounts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetDiscounts function");
            return new StatusCodeResult(500);
        }
    }    
}
