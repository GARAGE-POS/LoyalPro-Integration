using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Data;
using Karage.Functions.Services;

namespace Karage.Functions.Functions;

public class TestFunctions
{
    private readonly ILogger<TestFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly ISessionAuthService _sessionAuthService;

    public TestFunctions(
        ILogger<TestFunctions> logger,
        V1DbContext context,
        ISessionAuthService sessionAuthService)
    {
        _logger = logger;
        _context = context;
        _sessionAuthService = sessionAuthService;
    }

    [Function("TestSupplierData")]
    public async Task<IActionResult> TestSupplierData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        try
        {
            // Verify session and get session data
            var (authResult, sessionData) = await _sessionAuthService.VerifySessionAndGetData(req);
            if (authResult != null)
            {
                return authResult; // Return unauthorized result
            }

            if (sessionData == null)
            {
                return new BadRequestObjectResult(new { error = "Failed to retrieve session data" });
            }

            var userId = sessionData.UserID;
            var locationId = sessionData.LocationID;

            // Read-only queries to check current state
            var supplierCount = await _context.Suppliers
                .Where(s => s.StatusID == 1 && s.UserID == userId)
                .CountAsync();

            var suppliers = await _context.Suppliers
                .Where(s => s.StatusID == 1 && s.UserID == userId)
                .Select(s => new { s.SupplierID, s.Name, s.UserID })
                .Take(5)
                .ToListAsync();

            var mappingCount = await _context.SupplierMappings
                .Where(sm => sm.LocationId == locationId)
                .CountAsync();

            var mappings = await _context.SupplierMappings
                .Where(sm => sm.LocationId == locationId)
                .Select(sm => new { sm.SupplierId, sm.VomSupplierId, sm.LocationId })
                .Take(5)
                .ToListAsync();

            _logger.LogInformation("READ-ONLY TEST RESULTS:");
            _logger.LogInformation("User ID: {UserId}, Location ID: {LocationId}", userId, locationId);
            _logger.LogInformation("Total Suppliers for User {UserId}: {Count}", userId, supplierCount);
            _logger.LogInformation("Sample Suppliers: {@Suppliers}", suppliers);
            _logger.LogInformation("Total Supplier Mappings for Location {LocationId}: {Count}", locationId, mappingCount);
            _logger.LogInformation("Sample Mappings: {@Mappings}", mappings);

            return new OkObjectResult(new
            {
                message = "Database test completed - check logs for results",
                userId = userId,
                locationId = locationId,
                supplierCount = supplierCount,
                mappingCount = mappingCount,
                suppliers = suppliers,
                mappings = mappings
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSupplierData function");
            return new StatusCodeResult(500);
        }
    }
}