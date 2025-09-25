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

    [Function("SyncSuppliersToVom")]
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
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncSuppliersToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("SyncCategoriesToVom")]
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

            // TEMPORARY: Override to test with Location 404 which has categories
            locationId = 404;

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
                    // Try to find matching VOM category by name
                    var matchingVomCategory = vomCategories.FirstOrDefault(vc =>
                        !string.IsNullOrEmpty(vc.name) && vc.name.Equals(localCategory.name, StringComparison.OrdinalIgnoreCase));

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
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncCategoriesToVom function");
            return new StatusCodeResult(500);
        }
    }

    [Function("ClearInvalidProductMappings")]
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
                    .ThenInclude(sc => sc.Category)
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
                    CategoryID = i.SubCategory.CategoryID,
                    CategoryName = i.SubCategory.Category.Name
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
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncProductsToVom function");
            return new StatusCodeResult(500);
        }
    }
}