using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Data;
using Karage.Functions.Services;

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

            // Get total count first
            int totalCount = await _context.Items
                .Include(i => i.SubCategory)
                    .ThenInclude(sc => sc!.Category)
                        .ThenInclude(c => c!.Location)
                            .ThenInclude(l => l!.User)
                .Where(i => i.StatusID == 1 &&
                           i.SubCategory!.StatusID == 1 &&
                           i.SubCategory.Category!.StatusID == 1 &&
                           i.SubCategory.Category.Location!.User!.UserID == userId)
                .CountAsync();

            // Execute the query with pagination
            var products = await _context.Items
                .Include(i => i.SubCategory)
                    .ThenInclude(sc => sc!.Category)
                        .ThenInclude(c => c!.Location)
                            .ThenInclude(l => l!.User)
                .Where(i => i.StatusID == 1 &&
                           i.SubCategory!.StatusID == 1 &&
                           i.SubCategory.Category!.StatusID == 1 &&
                           i.SubCategory.Category.Location!.User!.UserID == userId)
                .Select(i => new
                {
                    itemID = i.ItemID,
                    name = i.Name,
                    locationID = i.SubCategory!.Category!.Location!.LocationID
                })
                .OrderByDescending(i => i.itemID)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

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