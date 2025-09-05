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
            var verificationResult = await _apiKeyService.VerifyApiKey(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            var locationIdStr = req.Query["locationId"].ToString();
            var userIdStr = req.Query["userId"].ToString();

            // Default values based on the SQL query
            int locationId = 1;
            int userId = 2;

            if (!string.IsNullOrEmpty(locationIdStr) && int.TryParse(locationIdStr, out int parsedLocationId))
            {
                locationId = parsedLocationId;
            }

            if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int parsedUserId))
            {
                userId = parsedUserId;
            }

            // Execute the query equivalent to the SQL provided
            var products = await _context.Items
                .Include(i => i.SubCategory)
                    .ThenInclude(sc => sc!.Category)
                        .ThenInclude(c => c!.Location)
                            .ThenInclude(l => l!.User)
                .Where(i => i.StatusID == 1 &&
                           i.SubCategory!.StatusID == 1 &&
                           i.SubCategory.Category!.StatusID == 1 &&
                           i.SubCategory.Category.LocationID == locationId &&
                           i.SubCategory.Category.Location!.User!.UserID == userId)
                .Select(i => new
                {
                    // Item fields
                    i.ItemID,
                    i.Name,
                    i.NameOnReceipt,
                    i.Description,
                    i.ItemImage,
                    i.Barcode,
                    i.SKU,
                    i.Price,
                    i.Cost,
                    i.ItemType,
                    i.FeaturedItem,
                    i.DisplayOrder,
                    
                    // SubCategory fields
                    SubCategory = new
                    {
                        i.SubCategory!.SubCategoryID,
                        i.SubCategory.Name,
                        i.SubCategory.Description,
                        i.SubCategory.SubImage,
                        i.SubCategory.DisplayOrder
                    },
                    
                    // Category fields
                    Category = new
                    {
                        i.SubCategory.Category!.CategoryID,
                        i.SubCategory.Category.Name,
                        i.SubCategory.Category.Description,
                        i.SubCategory.Category.Image,
                        i.SubCategory.Category.DisplayOrder
                    },
                    
                    // Location fields
                    Location = new
                    {
                        i.SubCategory.Category.Location!.LocationID,
                        i.SubCategory.Category.Location.Name,
                        i.SubCategory.Category.Location.Address,
                        i.SubCategory.Category.Location.ContactNo,
                        i.SubCategory.Category.Location.Email
                    },
                    
                    // User fields
                    User = new
                    {
                        i.SubCategory.Category.Location.User!.UserID,
                        i.SubCategory.Category.Location.User.UserName,
                        i.SubCategory.Category.Location.User.Company,
                        i.SubCategory.Category.Location.User.Email
                    }
                })
                .OrderBy(i => i.Category.DisplayOrder)
                .ThenBy(i => i.SubCategory.DisplayOrder)
                .ThenBy(i => i.DisplayOrder)
                .ThenBy(i => i.Name)
                .ToListAsync();

            if (!products.Any())
            {
                return new NotFoundObjectResult(new { message = "No products found for the specified location and user" });
            }

            return new OkObjectResult(new
            {
                totalCount = products.Count,
                locationId = locationId,
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