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
    [OpenApiOperation(operationId: "SyncUnitsToVom", tags: new[] { "VOM Integration" }, Summary = "Sync units to VOM", Description = "Synchronizes local units to VOM accounting system. Use Authorization header with format: Bearer POS-xxxxx")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization", Description = "Session token in format: Bearer POS-xxxxx")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(VomSyncResponse), Description = "Units synced successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Bad request or sync error")]
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
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncUnitsToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("SyncSuppliersToVom")]
    [OpenApiOperation(operationId: "SyncSuppliersToVom", tags: new[] { "VOM Integration" }, Summary = "Sync suppliers to VOM", Description = "Synchronizes local suppliers to VOM accounting system")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization", Description = "Session token in format: Bearer POS-xxxxx")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(VomSyncResponse), Description = "Suppliers synced successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    public async Task<IActionResult> SyncSuppliersToVom(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Sync suppliers to Vom endpoint called.");

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

            // Step 1: Get all suppliers from VOM
            var vomSuppliers = await _vomApiService.GetAllSuppliersAsync();
            if (vomSuppliers == null)
            {
                return new BadRequestObjectResult(new { error = "Failed to retrieve suppliers from VOM API" });
            }

            // Step 2: Get all local suppliers with active status for this user
            var localSuppliers = await _context.Suppliers
                .Where(s => s.StatusID == 1 && s.UserID == userId) // Only active suppliers for this user
                .Select(s => new
                {
                    s.SupplierID,
                    name = s.Name ?? string.Empty,
                    email = s.Email,
                    phone = s.Phone,
                    website = s.Website,
                    address = s.Address,
                    company_name = s.CompanyName,
                    contact_person = s.ContactPerson,
                    type = s.Type,
                    notes = s.Remarks
                })
                .ToListAsync();

            if (!localSuppliers.Any())
            {
                return new BadRequestObjectResult(new { error = "No active suppliers found in local database" });
            }

            var results = new List<object>();
            var createdCount = 0;
            var matchedCount = 0;

            // Step 3: Match local suppliers with VOM suppliers and create mappings
            foreach (var localSupplier in localSuppliers)
            {
                try
                {
                    // Try to find matching VOM supplier by name
                    var matchingVomSupplier = vomSuppliers.FirstOrDefault(vs =>
                        !string.IsNullOrEmpty(vs.name) && vs.name.Equals(localSupplier.name, StringComparison.OrdinalIgnoreCase));

                    int vomSupplierId;

                    if (matchingVomSupplier != null)
                    {
                        // Supplier already exists in VOM, use existing ID
                        vomSupplierId = matchingVomSupplier.id;
                        matchedCount++;

                        results.Add(new
                        {
                            local_supplier_id = localSupplier.SupplierID,
                            vom_supplier_id = vomSupplierId,
                            status = "matched",
                            action = "existing"
                        });
                    }
                    else
                    {
                        // Supplier doesn't exist in VOM, create it with required fields
                        var vomRequest = new
                        {
                            name = localSupplier.name,
                            country_code = "SA", // Default to Saudi Arabia
                            account_receivable_id = 187, // Default account receivable ID from your example
                            opening_balance = 0, // Default opening balance
                            email = localSupplier.email,
                            phone = localSupplier.phone,
                            website = localSupplier.website,
                            address = localSupplier.address,
                            company_name = localSupplier.company_name,
                            contact_person = localSupplier.contact_person,
                            type = localSupplier.type,
                            notes = localSupplier.notes
                        };

                        _logger.LogInformation("Attempting to create supplier in VOM: {SupplierName} (ID: {SupplierId}) with payload: {@Payload}",
                            localSupplier.name, localSupplier.SupplierID, vomRequest);

                        var vomResponse = await _vomApiService.PostAsync<VomSupplier>("/api/purchases/suppliers", vomRequest);

                        if (vomResponse != null && vomResponse.id > 0)
                        {
                            vomSupplierId = vomResponse.id;
                            createdCount++;

                            _logger.LogInformation("Successfully created supplier in VOM: {SupplierName} -> VOM ID: {VomSupplierId}",
                                localSupplier.name, vomSupplierId);

                            results.Add(new
                            {
                                local_supplier_id = localSupplier.SupplierID,
                                vom_supplier_id = vomSupplierId,
                                status = "created",
                                action = "new"
                            });
                        }
                        else
                        {
                            _logger.LogError("Failed to create supplier {SupplierName} (ID: {SupplierId}) in VOM. Response was null or invalid",
                                localSupplier.name, localSupplier.SupplierID);

                            results.Add(new
                            {
                                local_supplier_id = localSupplier.SupplierID,
                                status = "failed",
                                error = "Failed to create supplier in VOM - check logs for details"
                            });
                            continue; // Skip mapping creation
                        }
                    }

                    // Step 4: Create or update mapping (suppliers can have different VOM mappings per location)
                    var existingMapping = await _context.SupplierMappings
                        .FirstOrDefaultAsync(sm => sm.SupplierId == localSupplier.SupplierID && sm.LocationId == locationId);

                    if (existingMapping != null)
                    {
                        // Update existing mapping
                        _logger.LogInformation("Updating existing mapping for Supplier ID {SupplierId} -> VOM Supplier ID {VomSupplierId}",
                            localSupplier.SupplierID, vomSupplierId);
                        existingMapping.VomSupplierId = vomSupplierId;
                        existingMapping.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // Create new mapping
                        _logger.LogInformation("Creating new mapping for Supplier ID {SupplierId} -> VOM Supplier ID {VomSupplierId} at Location {LocationId}",
                            localSupplier.SupplierID, vomSupplierId, locationId);

                        var mapping = new SupplierMapping
                        {
                            SupplierId = localSupplier.SupplierID,
                            VomSupplierId = vomSupplierId,
                            LocationId = locationId
                        };
                        _context.SupplierMappings.Add(mapping);
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully saved mapping for Supplier ID {SupplierId}", localSupplier.SupplierID);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing supplier {localSupplier.SupplierID}");
                    results.Add(new
                    {
                        local_supplier_id = localSupplier.SupplierID,
                        status = "failed",
                        error = ex.Message
                    });
                }
            }

            return new OkObjectResult(new
            {
                message = "Supplier sync completed",
                total_local_suppliers = localSuppliers.Count,
                total_vom_suppliers = vomSuppliers.Count,
                created_suppliers = createdCount,
                matched_suppliers = matchedCount,
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncSuppliersToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("SyncCategoriesToVom")]
    [OpenApiOperation(operationId: "SyncCategoriesToVom", tags: new[] { "VOM Integration" }, Summary = "Sync categories to VOM", Description = "Synchronizes local categories to VOM accounting system")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization", Description = "Session token in format: Bearer POS-xxxxx")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(VomSyncResponse), Description = "Categories synced successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    public async Task<IActionResult> SyncCategoriesToVom(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Sync categories to Vom endpoint called.");

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

            _logger.LogInformation("Session authenticated successfully. User: {UserId}, Location: {LocationId} (TESTING with Location 404)",
                userId, locationId);

            // Step 1: Get all categories from VOM
            var vomCategories = await _vomApiService.GetAllCategoriesAsync();
            if (vomCategories == null)
            {
                return new BadRequestObjectResult(new { error = "Failed to retrieve categories from VOM API" });
            }

            _logger.LogInformation("Retrieved {VomCategoryCount} categories from VOM API", vomCategories.Count);

            // Step 2: Debug - Check all categories in database first
            var allLocalCategories = await _context.Categories.ToListAsync();
            _logger.LogInformation("Total categories in database: {TotalCategories}", allLocalCategories.Count);

            // Debug - Check categories by location
            var categoriesByLocation = allLocalCategories.GroupBy(c => c.LocationID ?? -1).ToDictionary(g => g.Key, g => g.Count());
            foreach (var locationGroup in categoriesByLocation)
            {
                _logger.LogInformation("Location {LocationId}: {CategoryCount} categories", locationGroup.Key == -1 ? "NULL" : locationGroup.Key.ToString(), locationGroup.Value);
            }

            // Debug - Check active categories for this specific location
            var activeLocalCategories = await _context.Categories
                .Where(c => c.StatusID == 1 && c.LocationID == locationId)
                .ToListAsync();
            _logger.LogInformation("Active categories for Location {LocationId}: {ActiveCategoryCount}", locationId, activeLocalCategories.Count);

            // Debug - Check categories without location filter
            var allActiveCategories = await _context.Categories
                .Where(c => c.StatusID == 1)
                .ToListAsync();
            _logger.LogInformation("Total active categories (all locations): {ActiveCategoryCount}", allActiveCategories.Count);

            // Step 2: Get all local categories with active status for this location
            var localCategories = await _context.Categories
                .Where(c => c.StatusID == 1 && c.LocationID == locationId) // Only active categories for this location
                .Select(c => new
                {
                    c.CategoryID,
                    name = c.Name ?? string.Empty,
                    description = c.Description,
                    image = c.Image,
                    sort_order = c.DisplayOrder
                })
                .ToListAsync();

            if (!localCategories.Any())
            {
                return new BadRequestObjectResult(new {
                    error = "No active categories found in local database for this location",
                    debug_info = new {
                        total_categories = allLocalCategories.Count,
                        total_active_categories = allActiveCategories.Count,
                        categories_for_location = activeLocalCategories.Count,
                        vom_categories = vomCategories.Count,
                        location_id = locationId
                    }
                });
            }

            var results = new List<object>();
            var createdCount = 0;
            var matchedCount = 0;

            // Step 3: Match local categories with VOM categories and create mappings
            foreach (var localCategory in localCategories)
            {
                try
                {
                    // Try to find matching VOM category by name_en (VOM API returns name_en/name_ar fields)
                    var matchingVomCategory = vomCategories.FirstOrDefault(vc =>
                        !string.IsNullOrEmpty(vc.name_en) && vc.name_en.Equals(localCategory.name, StringComparison.OrdinalIgnoreCase));

                    int vomCategoryId;

                    if (matchingVomCategory != null)
                    {
                        // Category already exists in VOM, use existing ID
                        vomCategoryId = matchingVomCategory.id;
                        matchedCount++;

                        results.Add(new
                        {
                            local_category_id = localCategory.CategoryID,
                            vom_category_id = vomCategoryId,
                            status = "matched",
                            action = "existing"
                        });
                    }
                    else
                    {
                        // Category doesn't exist in VOM, create it
                        var vomRequest = new
                        {
                            name_en = localCategory.name,  // VOM requires name_en field
                            name_ar = localCategory.name,  // VOM requires name_ar field
                            description = localCategory.description,
                            image = localCategory.image,
                            sort_order = localCategory.sort_order ?? 0,
                            is_active = true
                        };

                        _logger.LogInformation("Attempting to create category in VOM: {CategoryName} (ID: {CategoryId}) with payload: {@Payload}",
                            localCategory.name, localCategory.CategoryID, vomRequest);

                        var vomResponse = await _vomApiService.PostAsync<VomCategory>("/api/products/categories", vomRequest);

                        if (vomResponse != null && vomResponse.id > 0)
                        {
                            vomCategoryId = vomResponse.id;
                            createdCount++;

                            _logger.LogInformation("Successfully created category in VOM: {CategoryName} -> VOM ID: {VomCategoryId}",
                                localCategory.name, vomCategoryId);

                            results.Add(new
                            {
                                local_category_id = localCategory.CategoryID,
                                vom_category_id = vomCategoryId,
                                status = "created",
                                action = "new"
                            });
                        }
                        else
                        {
                            _logger.LogError("Failed to create category {CategoryName} (ID: {CategoryId}) in VOM. Response was null or invalid",
                                localCategory.name, localCategory.CategoryID);

                            results.Add(new
                            {
                                local_category_id = localCategory.CategoryID,
                                status = "failed",
                                error = "Failed to create category in VOM - check logs for details"
                            });
                            continue; // Skip mapping creation
                        }
                    }

                    // Step 4: Create or update mapping
                    var existingMapping = await _context.CategoryMappings
                        .FirstOrDefaultAsync(cm => cm.CategoryId == localCategory.CategoryID && cm.LocationId == locationId);

                    if (existingMapping != null)
                    {
                        // Update existing mapping
                        _logger.LogInformation("Updating existing mapping for Category ID {CategoryId} -> VOM Category ID {VomCategoryId}",
                            localCategory.CategoryID, vomCategoryId);
                        existingMapping.VomCategoryId = vomCategoryId;
                        existingMapping.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // Create new mapping
                        _logger.LogInformation("Creating new mapping for Category ID {CategoryId} -> VOM Category ID {VomCategoryId} at Location {LocationId}",
                            localCategory.CategoryID, vomCategoryId, locationId);

                        var mapping = new CategoryMapping
                        {
                            CategoryId = localCategory.CategoryID,
                            VomCategoryId = vomCategoryId,
                            LocationId = locationId
                        };
                        _context.CategoryMappings.Add(mapping);
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully saved mapping for Category ID {CategoryId}", localCategory.CategoryID);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing category {localCategory.CategoryID}");
                    results.Add(new
                    {
                        local_category_id = localCategory.CategoryID,
                        status = "failed",
                        error = ex.Message
                    });
                }
            }

            return new OkObjectResult(new
            {
                message = "Category sync completed",
                total_local_categories = localCategories.Count,
                total_vom_categories = vomCategories.Count,
                created_categories = createdCount,
                matched_categories = matchedCount,
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncCategoriesToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("ClearInvalidProductMappings")]
    [OpenApiOperation(operationId: "ClearInvalidProductMappings", tags: new[] { "VOM Integration" }, Summary = "Clear invalid product mappings", Description = "Removes product mappings with invalid VOM product IDs (-1)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Invalid mappings cleared successfully")]
    public async Task<IActionResult> ClearInvalidProductMappings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Clear invalid product mappings endpoint called.");

        try
        {
            var invalidMappings = await _context.ProductMappings
                .Where(pm => pm.VomProductId == -1)
                .ToListAsync();

            if (invalidMappings.Any())
            {
                _context.ProductMappings.RemoveRange(invalidMappings);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Cleared {Count} invalid product mappings", invalidMappings.Count);
            }

            return new OkObjectResult(new {
                message = "Invalid product mappings cleared",
                cleared_count = invalidMappings.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing invalid product mappings");
            return new StatusCodeResult(500);
        }
    }

    [Function("SyncProductsToVom")]
    [OpenApiOperation(operationId: "SyncProductsToVom", tags: new[] { "VOM Integration" }, Summary = "Sync products to VOM", Description = "Synchronizes local products to VOM accounting system")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(VomSyncResponse), Description = "Products synced successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    public async Task<IActionResult> SyncProductsToVom(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Sync products to Vom endpoint called.");

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

            // Step 1: First, let's see what categories exist for this location
            var categoriesForLocation = await _context.Categories
                .Where(c => c.LocationID == locationId && c.StatusID == 1)
                .Select(c => new { c.CategoryID, c.Name })
                .ToListAsync();

            _logger.LogInformation("Found {CategoryCount} categories for location {LocationId}: {Categories}",
                categoriesForLocation.Count, locationId, string.Join(", ", categoriesForLocation.Select(c => $"{c.CategoryID}:{c.Name}")));

            // Step 2: Get products from local database with proper category filtering by location
            var localProducts = await _context.Items
                .Include(i => i.SubCategory)
                    .ThenInclude(sc => sc!.Category)
                .Where(i => i.StatusID == 1 &&
                           i.SubCategory != null &&
                           i.SubCategory.Category != null &&
                           i.SubCategory.Category.LocationID == locationId)
                .Select(i => new {
                    i.ItemID,
                    i.Name,
                    i.Description,
                    i.Cost,
                    i.Price,
                    i.Barcode,
                    i.UnitID,
                    CategoryID = i.SubCategory!.CategoryID,
                    CategoryName = i.SubCategory!.Category!.Name
                })
                .ToListAsync();

            _logger.LogInformation("Found {ProductCount} active products for location {LocationId}: {Products}",
                localProducts.Count, locationId, string.Join(", ", localProducts.Select(p => $"{p.ItemID}:{p.Name}({p.CategoryName})")));

            if (!localProducts.Any())
            {
                return new BadRequestObjectResult(new {
                    error = "No active products found in local database"
                });
            }

            // Step 4: Get all products from VOM - test if we can find existing products
            _logger.LogInformation("Attempting to retrieve existing products from VOM...");
            var vomProducts = await _vomApiService.GetAllProductsAsync();
            if (vomProducts == null)
            {
                vomProducts = new List<VomProduct>(); // Initialize as empty if null
            }
            _logger.LogInformation("Retrieved {ProductCount} products from VOM API", vomProducts.Count);

            var results = new List<object>();
            var createdCount = 0;
            var matchedCount = 0;
            var failedCount = 0;
            var missingCategoryMappings = 0;
            var missingUnitMappings = 0;

            // Step 5: Process each product
            foreach (var localProduct in localProducts)
            {
                try
                {
                    _logger.LogInformation("Processing product: {ProductName} (ID: {ItemId})", localProduct.Name, localProduct.ItemID);

                    // Check for existing VOM product mapping first
                    var existingMapping = await _context.ProductMappings
                        .FirstOrDefaultAsync(pm => pm.ItemId == localProduct.ItemID && pm.LocationId == locationId);

                    if (existingMapping != null)
                    {
                        matchedCount++;
                        results.Add(new
                        {
                            local_item_id = localProduct.ItemID,
                            vom_product_id = existingMapping.VomProductId,
                            status = "already_mapped",
                            action = "existing_mapping"
                        });
                        _logger.LogInformation("Product {ProductName} already has VOM mapping: {VomProductId}",
                            localProduct.Name, existingMapping.VomProductId);
                        continue;
                    }

                    // Try to find matching VOM product by barcode or name
                    VomProduct? matchingVomProduct = null;

                    if (!string.IsNullOrEmpty(localProduct.Barcode))
                    {
                        matchingVomProduct = vomProducts.FirstOrDefault(vp =>
                            !string.IsNullOrEmpty(vp.barcode) && vp.barcode.Equals(localProduct.Barcode, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchingVomProduct == null)
                    {
                        matchingVomProduct = vomProducts.FirstOrDefault(vp =>
                            !string.IsNullOrEmpty(vp.name_en) && vp.name_en.Equals(localProduct.Name, StringComparison.OrdinalIgnoreCase));
                    }

                    int vomProductId;

                    if (matchingVomProduct != null)
                    {
                        // Product already exists in VOM, use existing ID
                        vomProductId = matchingVomProduct.id;
                        matchedCount++;

                        results.Add(new
                        {
                            local_item_id = localProduct.ItemID,
                            vom_product_id = vomProductId,
                            status = "matched",
                            action = "existing"
                        });

                        _logger.LogInformation("Found matching VOM product for {ProductName}: VOM ID {VomProductId}",
                            localProduct.Name, vomProductId);
                    }
                    else
                    {
                        // Product doesn't exist in VOM, need to create it

                        // First, resolve category mapping
                        int? vomCategoryId = null;
                        if (localProduct.CategoryID > 0)
                        {
                            var categoryMapping = await _context.CategoryMappings
                                .FirstOrDefaultAsync(cm => cm.CategoryId == localProduct.CategoryID && cm.LocationId == locationId);

                            if (categoryMapping != null)
                            {
                                vomCategoryId = categoryMapping.VomCategoryId;
                            }
                            else
                            {
                                missingCategoryMappings++;
                                _logger.LogWarning("No VOM category mapping found for local CategoryID {CategoryId} at location {LocationId}. Using default category.",
                                    localProduct.CategoryID, locationId);
                                vomCategoryId = 1; // Default category
                            }
                        }
                        else
                        {
                            vomCategoryId = 1; // Default category if no local category
                        }

                        // Resolve unit mapping
                        int? vomUnitId = null;
                        if (localProduct.UnitID.HasValue && localProduct.UnitID.Value > 0)
                        {
                            var unitMapping = await _context.UnitMappings
                                .FirstOrDefaultAsync(um => um.UnitId == localProduct.UnitID && um.LocationId == locationId);

                            if (unitMapping != null)
                            {
                                vomUnitId = unitMapping.VomUnitId;
                            }
                            else
                            {
                                missingUnitMappings++;
                                _logger.LogWarning("No VOM unit mapping found for local UnitID {UnitId} at location {LocationId}. Using default unit.",
                                    localProduct.UnitID, locationId);
                                vomUnitId = 4; // Default piece unit (based on existing code)
                            }
                        }
                        else
                        {
                            vomUnitId = 4; // Default piece unit
                        }

                        // Create VOM product request with minimal required fields
                        var vomRequest = new
                        {
                            name_en = localProduct.Name ?? "Unknown Product",
                            name_ar = localProduct.Name ?? "Unknown Product", // Use same as English since we don't have Arabic
                            category_id = vomCategoryId.ToString(),
                            unit_id = vomUnitId.ToString(),
                            type = "product" // Required field
                        };

                        _logger.LogInformation("Attempting to create product in VOM: {ProductName} (ID: {ItemId}) with payload: {@Payload}",
                            localProduct.Name, localProduct.ItemID, vomRequest);

                        var vomResponse = await _vomApiService.PostAsync<VomProduct>("/api/products/products", vomRequest);

                        if (vomResponse != null && vomResponse.id > 0)
                        {
                            vomProductId = vomResponse.id;
                            createdCount++;

                            _logger.LogInformation("Successfully created product in VOM: {ProductName} -> VOM ID: {VomProductId}",
                                localProduct.Name, vomProductId);

                            results.Add(new
                            {
                                local_item_id = localProduct.ItemID,
                                vom_product_id = vomProductId,
                                status = "created",
                                action = "new"
                            });
                        }
                        else
                        {
                            // Check if this is a "name already taken" error by making a raw POST call to get error details
                            var rawResponse = await _vomApiService.PostAsync("/api/products/products", vomRequest);
                            var responseContent = await rawResponse.Content.ReadAsStringAsync();

                            _logger.LogInformation("VOM API response for product {ProductName}: Status={Status}, Content={Content}",
                                localProduct.Name, rawResponse.StatusCode, responseContent);

                            // If the error is "name already taken", try to find the existing product
                            if (rawResponse.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity &&
                                responseContent.Contains("name has already been taken"))
                            {
                                _logger.LogInformation("Product {ProductName} already exists in VOM. Attempting to find actual product ID.",
                                    localProduct.Name);

                                // Search for the existing product to get its actual ID
                                var existingProduct = await _vomApiService.SearchProductByNameAsync(localProduct.Name ?? "");

                                if (existingProduct != null && existingProduct.id > 0)
                                {
                                    vomProductId = existingProduct.id;
                                    matchedCount++;

                                    _logger.LogInformation("Found existing product {ProductName} in VOM with ID: {VomProductId}",
                                        localProduct.Name, vomProductId);

                                    results.Add(new
                                    {
                                        local_item_id = localProduct.ItemID,
                                        vom_product_id = vomProductId,
                                        status = "existing_product",
                                        action = "found_existing",
                                        note = "Found existing product in VOM with actual ID"
                                    });
                                }
                                else
                                {
                                    // Fallback to placeholder if search fails
                                    vomProductId = -1; // Placeholder ID to indicate product exists in VOM
                                    matchedCount++;

                                    _logger.LogWarning("Product {ProductName} exists in VOM but could not find actual ID. Using placeholder.",
                                        localProduct.Name);

                                    results.Add(new
                                    {
                                        local_item_id = localProduct.ItemID,
                                        vom_product_id = vomProductId,
                                        status = "existing_product",
                                        action = "placeholder_mapping",
                                        note = "Product exists in VOM but exact ID unknown - using placeholder"
                                    });
                                }
                            }
                            else
                            {
                                failedCount++;
                                _logger.LogError("Failed to create product {ProductName} (ID: {ItemId}) in VOM. Status: {Status}, Response: {Response}",
                                    localProduct.Name, localProduct.ItemID, rawResponse.StatusCode, responseContent);

                                results.Add(new
                                {
                                    local_item_id = localProduct.ItemID,
                                    status = "failed",
                                    error = $"Failed to create product in VOM - {rawResponse.StatusCode}: {responseContent}"
                                });
                                continue; // Skip mapping creation
                            }
                        }
                    }

                    // Step 6: Create or update mapping
                    var mapping = new ProductMapping
                    {
                        ItemId = localProduct.ItemID,
                        VomProductId = vomProductId,
                        LocationId = locationId
                    };
                    _context.ProductMappings.Add(mapping);

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully saved mapping for Product ID {ItemId} -> VOM Product ID {VomProductId}",
                        localProduct.ItemID, vomProductId);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError(ex, $"Error processing product {localProduct.ItemID}");
                    results.Add(new
                    {
                        local_item_id = localProduct.ItemID,
                        status = "failed",
                        error = ex.Message
                    });
                }
            }

            return new OkObjectResult(new
            {
                message = "Product sync completed",
                total_local_products = localProducts.Count,
                total_vom_products = vomProducts.Count,
                created_products = createdCount,
                matched_products = matchedCount,
                failed_products = failedCount,
                missing_category_mappings = missingCategoryMappings,
                missing_unit_mappings = missingUnitMappings,
                user_id = userId,
                location_id = locationId,
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncProductsToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("SyncBillsToVom")]
    [OpenApiOperation(operationId: "SyncBillsToVom", tags: new[] { "VOM Integration" }, Summary = "Sync bills to VOM", Description = "Synchronizes local purchase bills to VOM accounting system")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Bills synced successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    public async Task<IActionResult> SyncBillsToVom(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("Sync bills to Vom endpoint called.");

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

            // Step 1: Get all purchase bills from VOM (for matching existing bills)
            var vomBills = await _vomApiService.GetAllPurchaseBillsAsync() ?? [];
            _logger.LogInformation("Retrieved {BillCount} bills from VOM API", vomBills.Count);

            // Step 2: Get all local bills with active status for this location
            var localBills = await _context.Bills
                .Include(b => b.BillDetails)
                    .ThenInclude(bd => bd.Item)
                .Include(b => b.Supplier)
                .Where(b => (b.StatusID == 1 || b.StatusID == 600) && b.LocationID == locationId) // Active bills and StatusID 600 for this location
                .Select(b => new
                {
                    b.BillID,
                    b.BillNo,
                    b.Date,
                    b.DueDate,
                    b.Remarks,
                    b.SubTotal,
                    b.Discount,
                    b.Tax,
                    b.Total,
                    b.SupplierID,
                    SupplierName = b.Supplier != null ? b.Supplier.Name : null,
                    BillDetails = b.BillDetails.Where(bd => bd.StatusID == 1 || bd.StatusID == 600).Select(bd => new
                    {
                        bd.ItemID,
                        ItemName = bd.Item != null ? bd.Item.Name : null,
                        bd.Quantity,
                        bd.Cost,
                        bd.Price,
                        bd.Total,
                        bd.Remarks
                    }).ToList()
                })
                .ToListAsync();

            if (localBills.Count == 0)
            {
                return new BadRequestObjectResult(new { error = "No active bills found in local database for this location" });
            }

            _logger.LogInformation("Found {BillCount} active bills for location {LocationId}", localBills.Count, locationId);

            var results = new List<object>();
            var createdCount = 0;
            var matchedCount = 0;
            var failedCount = 0;
            var missingSupplierMappings = 0;
            var missingProductMappings = 0;

            // Step 3: Process each bill
            foreach (var localBill in localBills)
            {
                try
                {
                    _logger.LogInformation("Processing bill: {BillNo} (ID: {BillId})", localBill.BillNo, localBill.BillID);

                    // Check for existing VOM bill mapping first
                    var existingMapping = await _context.BillMappings
                        .FirstOrDefaultAsync(bm => bm.BillId == localBill.BillID && bm.LocationId == locationId);

                    if (existingMapping != null)
                    {
                        matchedCount++;
                        results.Add(new
                        {
                            local_bill_id = localBill.BillID,
                            vom_bill_id = existingMapping.VomBillId,
                            status = "already_mapped",
                            action = "existing_mapping"
                        });
                        _logger.LogInformation("Bill {BillNo} already has VOM mapping: {VomBillId}",
                            localBill.BillNo, existingMapping.VomBillId);
                        continue;
                    }

                    // Try to find matching VOM bill by bill number or date
                    VomBill? matchingVomBill = null;

                    if (!string.IsNullOrEmpty(localBill.BillNo))
                    {
                        matchingVomBill = vomBills.FirstOrDefault(vb =>
                            !string.IsNullOrEmpty(vb.bill_no) && vb.bill_no.Equals(localBill.BillNo, StringComparison.OrdinalIgnoreCase));
                    }

                    int vomBillId;

                    if (matchingVomBill != null)
                    {
                        // Bill already exists in VOM, use existing ID
                        vomBillId = matchingVomBill.id;
                        matchedCount++;

                        results.Add(new
                        {
                            local_bill_id = localBill.BillID,
                            vom_bill_id = vomBillId,
                            status = "matched",
                            action = "existing"
                        });

                        _logger.LogInformation("Found matching VOM bill for {BillNo}: VOM ID {VomBillId}",
                            localBill.BillNo, vomBillId);
                    }
                    else
                    {
                        // Bill doesn't exist in VOM, need to create it

                        // First, resolve supplier mapping
                        int? vomSupplierId = null;
                        if (localBill.SupplierID.HasValue && localBill.SupplierID.Value > 0)
                        {
                            var supplierMapping = await _context.SupplierMappings
                                .FirstOrDefaultAsync(sm => sm.SupplierId == localBill.SupplierID && sm.LocationId == locationId);

                            if (supplierMapping != null)
                            {
                                vomSupplierId = supplierMapping.VomSupplierId;
                            }
                            else
                            {
                                missingSupplierMappings++;
                                _logger.LogWarning("No VOM supplier mapping found for local SupplierID {SupplierId} at location {LocationId}.",
                                    localBill.SupplierID, locationId);
                                // Skip this bill if no supplier mapping found
                                failedCount++;
                                results.Add(new
                                {
                                    local_bill_id = localBill.BillID,
                                    status = "failed",
                                    error = "Missing supplier mapping - sync suppliers first"
                                });
                                continue;
                            }
                        }

                        // Prepare bill items with mapped product IDs
                        var vomBillItems = new List<object>();
                        bool hasProductMappingIssues = false;

                        foreach (var billDetail in localBill.BillDetails)
                        {
                            if (billDetail.ItemID.HasValue)
                            {
                                // Try to find product mapping
                                var productMapping = await _context.ProductMappings
                                    .FirstOrDefaultAsync(pm => pm.ItemId == billDetail.ItemID && pm.LocationId == locationId);

                                if (productMapping != null)
                                {
                                    // For testing: use VOM product ID 181 if mapping is placeholder (-1)
                                    var vomProductId = productMapping.VomProductId > 0 ? productMapping.VomProductId : 181;

                                    vomBillItems.Add(new
                                    {
                                        product_id = vomProductId,
                                        quantity = billDetail.Quantity ?? 1,
                                        unit_price = billDetail.Cost ?? 0,
                                        total = billDetail.Total ?? 0,
                                        notes = billDetail.Remarks
                                    });
                                }
                                else
                                {
                                    missingProductMappings++;
                                    hasProductMappingIssues = true;
                                    _logger.LogWarning("No VOM product mapping found for ItemID {ItemId} (Name: {ItemName}) in bill {BillNo}",
                                        billDetail.ItemID, billDetail.ItemName, localBill.BillNo);
                                }
                            }
                        }

                        // Skip bill if there are product mapping issues and no items can be mapped
                        if (hasProductMappingIssues && vomBillItems.Count == 0)
                        {
                            failedCount++;
                            results.Add(new
                            {
                                local_bill_id = localBill.BillID,
                                status = "failed",
                                error = "Missing product mappings for all items - sync products first"
                            });
                            continue;
                        }

                        // Create VOM bill request with available data
                        var vomRequest = new
                        {
                            bill_no = localBill.BillNo ?? $"BILL-{localBill.BillID}",
                            code = localBill.BillNo ?? $"BILL-{localBill.BillID}", // Required: Bill code/reference
                            date = localBill.Date?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
                            due_date = localBill.DueDate?.ToString("yyyy-MM-dd") ?? DateTime.Now.AddDays(30).ToString("yyyy-MM-dd"),
                            payment_date = localBill.Date?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"), // Required: Payment date
                            notes = localBill.Remarks ?? "",
                            supplier = vomSupplierId, // Required: Supplier ID
                            supplier_id = vomSupplierId,
                            warehouse_id = 1, // Default warehouse - should be configurable
                            items = vomBillItems,
                            products = vomBillItems, // Required: Products array (same as items)
                            subtotal = localBill.SubTotal ?? 0,
                            discount = localBill.Discount ?? 0,
                            tax = localBill.Tax ?? 0,
                            total = localBill.Total ?? 0,
                            remaining = localBill.Total ?? 0, // Required: Outstanding amount (initially same as total)
                            payment_term = 1, // Required: Payment terms (1=cash, 2=credit, etc.)
                            action = "save" // Required: Action to perform (save, draft, etc.)
                        };

                        _logger.LogInformation("Attempting to create bill in VOM: {BillNo} (ID: {BillId}) with {ItemCount} items",
                            localBill.BillNo, localBill.BillID, vomBillItems.Count);

                        var vomResponse = await _vomApiService.CreatePurchaseBillAsync(vomRequest);

                        if (vomResponse != null && vomResponse.id > 0)
                        {
                            vomBillId = vomResponse.id;
                            createdCount++;

                            _logger.LogInformation("Successfully created bill in VOM: {BillNo} -> VOM ID: {VomBillId}",
                                localBill.BillNo, vomBillId);

                            results.Add(new
                            {
                                local_bill_id = localBill.BillID,
                                vom_bill_id = vomBillId,
                                status = "created",
                                action = "new",
                                items_count = vomBillItems.Count,
                                missing_product_mappings = hasProductMappingIssues
                            });
                        }
                        else
                        {
                            failedCount++;
                            _logger.LogError("Failed to create bill {BillNo} (ID: {BillId}) in VOM. Response was null or invalid",
                                localBill.BillNo, localBill.BillID);

                            results.Add(new
                            {
                                local_bill_id = localBill.BillID,
                                status = "failed",
                                error = "Failed to create bill in VOM - check logs for details"
                            });
                            continue; // Skip mapping creation
                        }
                    }

                    // Step 4: Create or update mapping
                    var mapping = new BillMapping
                    {
                        BillId = localBill.BillID,
                        VomBillId = vomBillId,
                        LocationId = locationId
                    };
                    _context.BillMappings.Add(mapping);

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully saved mapping for Bill ID {BillId} -> VOM Bill ID {VomBillId}",
                        localBill.BillID, vomBillId);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError(ex, "Error processing bill {BillId}", localBill.BillID);
                    results.Add(new
                    {
                        local_bill_id = localBill.BillID,
                        status = "failed",
                        error = ex.Message
                    });
                }
            }

            return new OkObjectResult(new
            {
                message = "Bill sync completed",
                total_local_bills = localBills.Count,
                total_vom_bills = vomBills.Count,
                created_bills = createdCount,
                matched_bills = matchedCount,
                failed_bills = failedCount,
                missing_supplier_mappings = missingSupplierMappings,
                missing_product_mappings = missingProductMappings,
                user_id = userId,
                location_id = locationId,
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncBillsToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("GetBillsForLocation")]
    [OpenApiOperation(operationId: "GetBillsForLocation", tags: new[] { "VOM Integration" }, Summary = "Get bills for location", Description = "Retrieves all bills for the authenticated user's location")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Bills retrieved successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    public async Task<IActionResult> GetBillsForLocation([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        try
        {
            _logger.LogInformation("Get bills for location endpoint called.");

            // Get authorization header
            if (!req.Headers.TryGetValue("Authorization", out var authHeader) ||
                !authHeader.ToString().StartsWith("Bearer "))
            {
                return new UnauthorizedResult();
            }

            var (authResult, sessionData) = await _sessionAuthService.VerifySessionAndGetData(req);
            if (authResult != null)
            {
                return authResult;
            }

            var userId = sessionData!.UserID;
            var locationId = sessionData.LocationID;

            _logger.LogInformation("Session authenticated successfully. User: {UserId}, Location: {LocationId}",
                userId, locationId);

            // Get all bills for this location (regardless of status)
            var allBills = await _context.Bills
                .Include(b => b.BillDetails)
                    .ThenInclude(bd => bd.Item)
                .Where(b => b.LocationID == locationId)
                .Select(b => new
                {
                    b.BillID,
                    b.BillNo,
                    b.StatusID,
                    b.LocationID,
                    b.SupplierID,
                    b.Total,
                    b.Date,
                    BillItemsCount = b.BillDetails.Count(),
                    BillItems = b.BillDetails.Select(bd => new {
                        bd.ItemID,
                        ItemName = bd.Item != null ? bd.Item.Name : null,
                        bd.Quantity,
                        bd.StatusID
                    }).ToList()
                })
                .ToListAsync();

            // Also get bills with StatusID = 1 specifically
            var activeBills = await _context.Bills
                .Where(b => b.LocationID == locationId && b.StatusID == 1)
                .Select(b => new
                {
                    b.BillID,
                    b.BillNo,
                    b.StatusID,
                    b.LocationID,
                    b.SupplierID,
                    b.Total,
                    b.Date,
                    BillItemsCount = b.BillDetails.Count()
                })
                .ToListAsync();

            return new OkObjectResult(new
            {
                location_id = locationId,
                user_id = userId,
                all_bills_count = allBills.Count,
                active_bills_count = activeBills.Count,
                all_bills = allBills,
                active_bills = activeBills
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetBillsForLocation function");
            return new StatusCodeResult(500);
        }
    }

    [Function("UpdateBillsToVom")]
    [OpenApiOperation(operationId: "UpdateBillsToVom", tags: new[] { "VOM Integration" }, Summary = "Update bills in VOM", Description = "Updates existing bills in VOM from local reconciliation records")]
    [OpenApiSecurity("Authorization", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "Authorization")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Bills updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing session token")]
    public async Task<IActionResult> UpdateBillsToVom([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            _logger.LogInformation("Update bills to Vom endpoint called.");

            // Extract and validate session authentication
            var (authResult, sessionData) = await _sessionAuthService.VerifySessionAndGetData(req);
            if (authResult != null)
            {
                return authResult;
            }

            if (sessionData == null)
            {
                return new UnauthorizedObjectResult(new { error = "Invalid or expired session" });
            }

            int userId = sessionData.UserID;
            int locationId = sessionData.LocationID;

            _logger.LogInformation("Processing bill updates for user {UserId} at location {LocationId}", userId, locationId);

            // Get reconciliation records (bill updates) from local database
            var reconciliations = await _context.Reconciliations
                .Include(r => r.ReconciliationDetails)
                    .ThenInclude(rd => rd.Item)
                .Where(r => r.LocationID == locationId && (r.StatusID == 1 || r.StatusID == 600))
                .ToListAsync();

            _logger.LogInformation("Found {ReconciliationCount} reconciliation records to process", reconciliations.Count);

            var results = new List<object>();
            int updatedCount = 0;
            int failedCount = 0;

            foreach (var reconciliation in reconciliations)
            {
                try
                {
                    // Check if there's a corresponding VOM bill mapping for this reconciliation
                    // Assuming PurchaseOrderID maps to the original bill that was synced
                    var billMapping = await _context.BillMappings
                        .FirstOrDefaultAsync(bm => bm.BillId == reconciliation.PurchaseOrderID && bm.LocationId == locationId);

                    if (billMapping == null)
                    {
                        _logger.LogWarning("No VOM bill mapping found for reconciliation {ReconciliationId} with PurchaseOrderID {PurchaseOrderId}",
                            reconciliation.ReconciliationID, reconciliation.PurchaseOrderID);

                        results.Add(new
                        {
                            reconciliation_id = reconciliation.ReconciliationID,
                            status = "failed",
                            error = "No corresponding VOM bill mapping found"
                        });
                        failedCount++;
                        continue;
                    }

                    // Prepare update payload with reconciliation data
                    var vomUpdateItems = new List<object>();

                    foreach (var reconciliationDetail in reconciliation.ReconciliationDetails)
                    {
                        // Get VOM product mapping for the item
                        var productMapping = await _context.ProductMappings
                            .FirstOrDefaultAsync(pm => pm.ItemId == reconciliationDetail.ItemID && pm.LocationId == locationId);

                        var vomProductId = productMapping?.VomProductId > 0 ? productMapping.VomProductId : 181; // Use fallback

                        vomUpdateItems.Add(new
                        {
                            product_id = vomProductId,
                            quantity = reconciliationDetail.Quantity ?? 1,
                            unit_price = reconciliationDetail.Cost ?? 0,
                            total = reconciliationDetail.Total ?? 0,
                            notes = reconciliationDetail.Reason
                        });
                    }

                    // Create VOM update request
                    var vomUpdateRequest = new
                    {
                        code = reconciliation.Code ?? $"REC-{reconciliation.ReconciliationID}",
                        date = reconciliation.Date?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
                        notes = reconciliation.Reason ?? "",
                        warehouse_id = 1,
                        items = vomUpdateItems,
                        products = vomUpdateItems,
                        action = "update"
                    };

                    _logger.LogInformation("Attempting to update VOM bill {VomBillId} from reconciliation {ReconciliationId}",
                        billMapping.VomBillId, reconciliation.ReconciliationID);

                    var vomResponse = await _vomApiService.UpdatePurchaseBillAsync(billMapping.VomBillId, vomUpdateRequest);

                    if (vomResponse != null && vomResponse.id > 0)
                    {
                        updatedCount++;
                        _logger.LogInformation("Successfully updated VOM bill {VomBillId} from reconciliation {ReconciliationId}",
                            billMapping.VomBillId, reconciliation.ReconciliationID);

                        results.Add(new
                        {
                            reconciliation_id = reconciliation.ReconciliationID,
                            vom_bill_id = billMapping.VomBillId,
                            status = "updated",
                            action = "update"
                        });
                    }
                    else
                    {
                        failedCount++;
                        _logger.LogError("Failed to update VOM bill {VomBillId} from reconciliation {ReconciliationId}",
                            billMapping.VomBillId, reconciliation.ReconciliationID);

                        results.Add(new
                        {
                            reconciliation_id = reconciliation.ReconciliationID,
                            vom_bill_id = billMapping.VomBillId,
                            status = "failed",
                            error = "Failed to update bill in VOM - check logs for details"
                        });
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError(ex, "Exception updating reconciliation {ReconciliationId}", reconciliation.ReconciliationID);

                    results.Add(new
                    {
                        reconciliation_id = reconciliation.ReconciliationID,
                        status = "failed",
                        error = $"Exception: {ex.Message}"
                    });
                }
            }

            _logger.LogInformation("Bill update sync completed. Updated: {UpdatedCount}, Failed: {FailedCount}",
                updatedCount, failedCount);

            return new OkObjectResult(new
            {
                message = "Bill update sync completed",
                total_reconciliations = reconciliations.Count,
                updated_bills = updatedCount,
                failed_bills = failedCount,
                user_id = userId,
                location_id = locationId,
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateBillsToVom function");
            return new StatusCodeResult(500);
        }
    }
}

// OpenAPI Models for VOM functions
public class VomSyncResponse
{
    public string message { get; set; } = string.Empty;
    public int total_local_units { get; set; }
    public int total_vom_units { get; set; }
    public int created_units { get; set; }
    public int matched_units { get; set; }
    public List<object> results { get; set; } = new();
}

public class ErrorResponse
{
    public string error { get; set; } = string.Empty;
}