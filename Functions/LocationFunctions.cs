using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Data;
using Karage.Functions.Services;

namespace Karage.Functions.Functions;

public class LocationFunctions
{
    private readonly ILogger<LocationFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IApiKeyService _apiKeyService;

    public LocationFunctions(ILogger<LocationFunctions> logger, V1DbContext context, IApiKeyService apiKeyService)
    {
        _logger = logger;
        _context = context;
        _apiKeyService = apiKeyService;
    }

    [Function("GetLocationsForUser")]
    public async Task<IActionResult> GetLocationsForUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Get locations for user endpoint called.");

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
            int page = 1;
            int pageSize = 0; // 0 means no pagination (return all)
            
            if (req.Query.ContainsKey("page") && int.TryParse(req.Query["page"], out var parsedPage) && parsedPage > 0)
            {
                page = parsedPage;
            }
            
            if (req.Query.ContainsKey("pageSize") && int.TryParse(req.Query["pageSize"], out var parsedPageSize) && parsedPageSize > 0)
            {
                pageSize = Math.Min(parsedPageSize, 100); // Cap at 100 items per page
            }

            // Get total count for pagination metadata
            var totalCount = await _context.Locations
                .Where(l => l.UserID == userId && l.StatusID == 1)
                .CountAsync();

            if (totalCount == 0)
            {
                return new NotFoundObjectResult(new { message = "No locations found for the specified user" });
            }

            // Build the base query
            var query = _context.Locations
                .Where(l => l.UserID == userId && l.StatusID == 1)
                .Select(l => new
                {
                    l.LocationID,
                    l.Name,
                    l.Address,
                    l.ContactNo,
                    l.Email
                })
                .OrderBy(l => l.LocationID);

            // Apply pagination if specified
            var locations = pageSize > 0 
                ? await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()
                : await query.ToListAsync();

            // Build response object
            var response = new
            {
                totalCount = totalCount,
                userId = userId,
                user = new
                {
                    userID = user.UserID,
                    userName = user.UserName,
                    company = user.Company,
                    email = user.Email
                },
                locations = locations
            };

            // Add pagination metadata if pagination is being used
            if (pageSize > 0)
            {
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
                var paginatedResponse = new
                {
                    response.totalCount,
                    response.userId,
                    response.user,
                    response.locations,
                    pagination = new
                    {
                        currentPage = page,
                        pageSize = pageSize,
                        totalPages = totalPages,
                        hasNextPage = page < totalPages,
                        hasPreviousPage = page > 1
                    }
                };
                return new OkObjectResult(paginatedResponse);
            }

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving locations for user");
            return new StatusCodeResult(500);
        }
    }
}