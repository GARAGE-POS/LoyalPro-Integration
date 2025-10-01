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
    [OpenApiOperation(operationId: "GetLocationsForUser", tags: new[] { "Locations" }, Summary = "Get locations for user", Description = "Retrieves all active locations associated with the authenticated user")]
    [OpenApiSecurity("X-API-Key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Locations retrieved successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing API key")]
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

            var locations = await _context.Locations
                .Where(l => l.UserID == userId && l.StatusID == 1) // Assuming StatusID for active locations
                .Select(l => new
                {
                    locationId = l.LocationID,
                    name = l.Name
                })
                .ToListAsync();

            return new OkObjectResult(locations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving locations for user");
            return new StatusCodeResult(500);
        }
    }
}