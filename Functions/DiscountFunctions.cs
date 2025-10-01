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
    [OpenApiOperation(operationId: "GetDiscounts", tags: new[] { "Discounts" }, Summary = "Get discounts for location", Description = "Retrieves all active discounts for a specific location")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "LocationID", In = ParameterLocation.Query, Required = true, Type = typeof(int), Description = "Location ID to retrieve discounts for")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Discounts retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Missing or invalid LocationID")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing authorization token")]
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
