using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Karage.Functions.Functions;

public class VomFunctions
{
    private readonly ILogger<VomFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IApiKeyService _apiKeyService;
    private readonly ISessionAuthService _sessionAuthService;
    private readonly IVomApiService _vomApiService;
    private readonly HttpClient _httpClient;

    public VomFunctions(
        ILogger<VomFunctions> logger,
        V1DbContext context,
        IApiKeyService apiKeyService,
        ISessionAuthService sessionAuthService,
        IVomApiService vomApiService,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _context = context;
        _apiKeyService = apiKeyService;
        _sessionAuthService = sessionAuthService;
        _vomApiService = vomApiService;
        _httpClient = httpClientFactory.CreateClient();
    }

    [Function("SyncUnitsToVom")]
    public async Task<IActionResult> SyncUnitsToVom(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Sync units to Vom endpoint called.");

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

            var locationId = sessionData.LocationID;
            var userId = sessionData.UserID;

            _logger.LogInformation("Session authenticated successfully. User: {UserId}, Location: {LocationId}",
                userId, locationId);

            // Step 1: Get all units from VOM
            var vomUnits = await _vomApiService.GetAllUnitsAsync();
            if (vomUnits == null)
            {
                return new BadRequestObjectResult(new { error = "Failed to retrieve units from VOM API" });
            }

            // Step 2: Get all local units
            var localUnits = await _context.Units
                .Select(u => new
                {
                    u.UnitID,
                    name_en = u.UnitName,
                    name_ar = u.UnitName, // Using same as English since we don't have Arabic in local DB
                    symbol = u.UnitName, // Using UnitName as symbol
                    unit_type_id = 4 // Default unit type ID
                })
                .ToListAsync();

            if (!localUnits.Any())
            {
                return new BadRequestObjectResult(new { error = "No units found in local database" });
            }

            var results = new List<object>();
            var createdCount = 0;
            var matchedCount = 0;

            // Step 3: Match local units with VOM units and create mappings
            foreach (var localUnit in localUnits)
            {
                try
                {
                    // Try to find matching VOM unit primarily by name_en
                    var matchingVomUnit = vomUnits.FirstOrDefault(vu =>
                        !string.IsNullOrEmpty(vu.name_en) && vu.name_en.Equals(localUnit.name_en, StringComparison.OrdinalIgnoreCase));

                    int vomUnitId;

                    if (matchingVomUnit != null)
                    {
                        // Unit already exists in VOM, use existing ID
                        vomUnitId = matchingVomUnit.id;
                        matchedCount++;

                        results.Add(new
                        {
                            local_unit_id = localUnit.UnitID,
                            vom_unit_id = vomUnitId,
                            status = "matched",
                            action = "existing"
                        });
                    }
                    else
                    {
                        // Unit doesn't exist in VOM, create it with proper payload format
                        var vomRequest = new
                        {
                            name_en = localUnit.name_en,
                            name_ar = localUnit.name_ar,
                            symbol = localUnit.symbol,
                            unit_type_id = localUnit.unit_type_id
                        };

                        _logger.LogInformation("Attempting to create unit in VOM: {UnitName} (ID: {UnitId}) with payload: {@Payload}", localUnit.name_en, localUnit.UnitID, vomRequest);
                        var vomResponse = await _vomApiService.PostAsync<VomUnit>("/api/products/units", vomRequest);

                        if (vomResponse != null && vomResponse.id > 0)
                        {
                            vomUnitId = vomResponse.id;
                            createdCount++;

                            _logger.LogInformation("Successfully created unit in VOM: {UnitName} -> VOM ID: {VomUnitId}", localUnit.name_en, vomUnitId);
                            results.Add(new
                            {
                                local_unit_id = localUnit.UnitID,
                                vom_unit_id = vomUnitId,
                                status = "created",
                                action = "new"
                            });
                        }
                        else
                        {
                            _logger.LogError("Failed to create unit {UnitName} (ID: {UnitId}) in VOM. Response was null or invalid", localUnit.name_en, localUnit.UnitID);
                            results.Add(new
                            {
                                local_unit_id = localUnit.UnitID,
                                status = "failed",
                                error = "Failed to create unit in VOM - check logs for details"
                            });
                            continue; // Skip mapping creation
                        }
                    }

                    // Step 4: Create or update mapping
                    var existingMapping = await _context.UnitMappings
                        .FirstOrDefaultAsync(um => um.UnitId == localUnit.UnitID && um.LocationId == locationId);

                    if (existingMapping != null)
                    {
                        // Update existing mapping
                        _logger.LogInformation("Updating existing mapping for Unit ID {UnitId} -> VOM Unit ID {VomUnitId}", localUnit.UnitID, vomUnitId);
                        existingMapping.VomUnitId = vomUnitId;
                        existingMapping.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // Create new mapping
                        _logger.LogInformation("Creating new mapping for Unit ID {UnitId} -> VOM Unit ID {VomUnitId} at Location {LocationId}", localUnit.UnitID, vomUnitId, locationId);
                        var mapping = new UnitMapping
                        {
                            UnitId = localUnit.UnitID,
                            VomUnitId = vomUnitId,
                            LocationId = locationId
                        };
                        _context.UnitMappings.Add(mapping);
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully saved mapping for Unit ID {UnitId}", localUnit.UnitID);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing unit {localUnit.UnitID}");
                    results.Add(new
                    {
                        local_unit_id = localUnit.UnitID,
                        status = "failed",
                        error = ex.Message
                    });
                }
            }

            return new OkObjectResult(new
            {
                message = "Unit sync completed",
                total_local_units = localUnits.Count,
                total_vom_units = vomUnits.Count,
                created_units = createdCount,
                matched_units = matchedCount,
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncUnitsToVom function");
            return new StatusCodeResult(500);
        }
    }
}