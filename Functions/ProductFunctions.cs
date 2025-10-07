using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using System.Net;

namespace Karage.Functions.Functions;

public class ProductFunctions
{
    private readonly ILogger<ProductFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IApiKeyService _apiKeyService;

    public ProductFunctions(ILogger<ProductFunctions> logger, V1DbContext context, IApiKeyService apiKeyService)
    {
        _logger = logger;
        _context = context;
        _apiKeyService = apiKeyService;
    }

    [Function("GetProducts")]
    [OpenApiOperation(operationId: "GetProducts", tags: new[] { "Products" }, Summary = "Get products", Description = "Retrieves paginated list of products for the authenticated user's locations")]
    [OpenApiSecurity("X-API-Key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
    [OpenApiParameter(name: "page", In = ParameterLocation.Query, Required = false, Type = typeof(int), Description = "Page number (default: 1)")]
    [OpenApiParameter(name: "pageSize", In = ParameterLocation.Query, Required = false, Type = typeof(int), Description = "Number of items per page (default: 20, max: 100)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Products retrieved successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing API key")]
    public async Task<IActionResult> GetProducts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Get products endpoint called.");

        try
        {
            var (verificationResult, user) = await _apiKeyService.VerifyApiKeyAndGetUser(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            if (user == null)
            {
                return new UnauthorizedResult();
            }

            int userId = user.UserID;

            // Parse pagination parameters
            int page = int.TryParse(req.Query["page"], out var p) ? Math.Max(1, p) : 1;
            int pageSize = int.TryParse(req.Query["pageSize"], out var ps) ? Math.Clamp(ps, 1, 100) : 20;


            // Use MapUniqueItemID for unique product lookup
            // Only select products for locations owned by this user
            var userLocationIds = await _context.Locations
                .Where(l => l.UserID == userId)
                .Select(l => l.LocationID)
                .ToListAsync();


            var allItems = await _context.MapUniqueItemIDs
                .Where(m => userLocationIds.Contains(m.LocationID))
                .Join(_context.Items,
                    m => m.ItemID,
                    i => i.ItemID,
                    (m, i) => new { Mapping = m, Item = i })
                .ToListAsync();

            var grouped = allItems
                .GroupBy(x => x.Mapping.UniqueItemID)
                .Select(g => g.First())
                .OrderByDescending(x => x.Mapping.UniqueItemID)
                .ToList();

            int totalCount = grouped.Count;
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var products = grouped
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    itemID = x.Mapping.ItemID,
                    name = x.Mapping.ProductName,
                    price = x.Item.Price
                })
                .ToList();

            return new OkObjectResult(new
            {
                totalCount = totalCount,
                currentPage = page,
                pageSize = pageSize,
                totalPages = totalPages,
                userId = userId,
                products = products
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products");
            return new StatusCodeResult(500);
        }
    }
}