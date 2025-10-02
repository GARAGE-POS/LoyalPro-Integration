using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Karage.Functions.Services;

public interface IVomApiService
{
    Task<string?> GetAuthToken();
    Task<T?> GetAsync<T>(string endpoint);
    Task<T?> PostAsync<T>(string endpoint, object data);
    Task<HttpResponseMessage> PostAsync(string endpoint, object data);
    Task<List<VomUnit>?> GetAllUnitsAsync();
    Task<List<VomSupplier>?> GetAllSuppliersAsync();
    Task<List<VomCategory>?> GetAllCategoriesAsync();
    Task<List<VomProduct>?> GetAllProductsAsync();
    Task<VomProduct?> SearchProductByNameAsync(string productName);
    Task<List<VomBill>?> GetAllPurchaseBillsAsync();
    Task<VomBill?> CreatePurchaseBillAsync(object billData);
    Task<VomBill?> UpdatePurchaseBillAsync(int billId, object billData);
}

// VOM Unit models
public class VomUnit
{
    public int id { get; set; }
    public string? name_en { get; set; }
    public string? name_ar { get; set; }
    public string? symbol { get; set; }
    public int? unit_type_id { get; set; }
}

public class VomUnitsResponse
{
    public List<VomUnit>? data { get; set; }
}

// VOM Supplier models
public class VomSupplier
{
    public int id { get; set; }
    public string? name { get; set; }
    public string? email { get; set; }
    public string? phone { get; set; }
    public string? website { get; set; }
    public string? address { get; set; }
    public string? company_name { get; set; }
    public string? contact_person { get; set; }
    public string? type { get; set; }
    public string? notes { get; set; }
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public class VomSuppliersResponse
{
    public List<VomSupplier>? data { get; set; }
}

// VOM Category models
public class VomCategory
{
    public int id { get; set; }
    public string? name { get; set; }
    public string? name_en { get; set; }
    public string? name_ar { get; set; }
    public string? description { get; set; }
    public string? image { get; set; }
    public int? parent_id { get; set; }
    public int? sort_order { get; set; }
    public bool? is_active { get; set; }
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public class VomCategoriesResponse
{
    public List<VomCategory>? data { get; set; }
}

// VOM Product models - matching actual API response format
public class VomProduct
{
    public int id { get; set; }
    public string? name_en { get; set; }
    public string? name_ar { get; set; }
    public string? identifier { get; set; }
    public string? barcode { get; set; }
    public string? image_path { get; set; }
    public string? type { get; set; }
    public decimal? quantity { get; set; }
    public decimal? minimum_quantity { get; set; }
    public int? allow_notification { get; set; }
    public string? desc { get; set; }
    public decimal? selling_price { get; set; }
    public decimal? buying_price { get; set; }
    public object? total_quantity { get; set; }
    public object? total_cost { get; set; }
    public object? average_cost { get; set; }

    // VOM returns these as strings, so we handle them as strings and convert when needed
    public string? category_id { get; set; }
    public string? unit_id { get; set; }
    public int? inventory_account_id { get; set; }

    // Helper properties to get integer values
    public int? CategoryIdAsInt => int.TryParse(category_id, out var result) ? result : null;
    public int? UnitIdAsInt => int.TryParse(unit_id, out var result) ? result : null;
    public int? selling_account_id { get; set; }
    public int? purchasing_account_id { get; set; }
    public int? tax_id { get; set; }
    public object? custom_fields { get; set; }
    public object? additional_cost { get; set; }
    public int? additional_cost_account_id { get; set; }
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
    public DateTime? deleted_at { get; set; }
    public string? image { get; set; }
}

public class VomProductsResponse
{
    public List<VomProduct>? data { get; set; }
}

// VOM Purchase Bill models
public class VomBill
{
    public int id { get; set; }
    public string? bill_no { get; set; }
    public string? date { get; set; }
    public string? due_date { get; set; }
    public string? notes { get; set; }
    public decimal? subtotal { get; set; }
    public decimal? discount { get; set; }
    public decimal? tax { get; set; }
    public decimal? total { get; set; }
    public int? supplier_id { get; set; }
    public int? warehouse_id { get; set; }
    public string? status { get; set; }
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
    public List<VomBillItem>? items { get; set; }
}

public class VomBillItem
{
    public int? product_id { get; set; }
    public string? product_name { get; set; }
    public int? quantity { get; set; }
    public decimal? unit_price { get; set; }
    public decimal? total_price { get; set; }
    public string? unit { get; set; }
    public string? notes { get; set; }
}

public class VomBillsResponse
{
    public List<VomBill>? data { get; set; }
}

public class VomBillCreateResponse
{
    public VomBill? purchase_bill { get; set; }
}

public class VomApiService : IVomApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VomApiService> _logger;
    private const string BaseUrl = "https://nouravom.getvom.com";
    private const string ApiAgent = "ios";
    private const string AcceptLanguage = "en";
    
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public VomApiService(HttpClient httpClient, ILogger<VomApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> GetAuthToken()
    {
        // Check if we have a valid cached token
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedToken;
        }

        // Use VOM's login API to get authentication token
        var loginRequest = new
        {
            email = "Odai.alhasan88@gmail.com",
            password = "Aa1m7A5dMD5"
        };

        var jsonContent = JsonSerializer.Serialize(loginRequest);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/companyuser/login");
        request.Content = content;
        AddCommonHeaders(request);

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("VOM login API response: {Response}", responseContent);

            var loginResponse = JsonSerializer.Deserialize<VomLoginResponse>(responseContent);

            if (loginResponse?.success == true && !string.IsNullOrEmpty(loginResponse?.data?.token))
            {
                _cachedToken = loginResponse.data.token;
                // Set token to expire in 23 hours (VOM tokens typically last 24 hours)
                _tokenExpiry = DateTime.UtcNow.AddHours(23);
                _logger.LogInformation("Successfully obtained VOM token: {TokenStart}...", _cachedToken?.Substring(0, Math.Min(10, _cachedToken.Length)));
                return _cachedToken;
            }
        }

        _logger.LogError("Failed to authenticate with VOM API. Status: {StatusCode}", response.StatusCode);
        _cachedToken = null;
        _tokenExpiry = DateTime.MinValue;
        return null;
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{endpoint}");
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent);
        }

        _logger.LogError("GET request to {Endpoint} failed. Status: {StatusCode}", endpoint, response.StatusCode);
        return default;
    }

    public async Task<T?> PostAsync<T>(string endpoint, object data)
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("Cannot make POST request to {Endpoint} - no valid token", endpoint);
            return default;
        }

        var jsonContent = JsonSerializer.Serialize(data);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogInformation("Making POST request to {Endpoint} with data: {Data}", endpoint, jsonContent);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
        request.Content = content;
        AddCommonHeaders(request);
        var authHeader = $"Bearer {token}";
        request.Headers.Add("Authorization", authHeader);
        _logger.LogInformation("Using Authorization header: {AuthHeader}", authHeader.Substring(0, Math.Min(20, authHeader.Length)) + "...");

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("POST request to {Endpoint} succeeded. Response: {Response}", endpoint, responseContent);
            
            // Parse the VOM API response structure: { "status": 200, "data": {...}, "success": true }
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var successElement) && successElement.GetBoolean() &&
                root.TryGetProperty("data", out var dataElement))
            {
                // Handle product creation response which has nested "product" object
                if (typeof(T) == typeof(VomProduct) && dataElement.TryGetProperty("product", out var productElement))
                {
                    _logger.LogInformation("Product creation successful, parsing product from response");
                    return JsonSerializer.Deserialize<T>(productElement.GetRawText());
                }
                // Handle regular responses
                return JsonSerializer.Deserialize<T>(dataElement.GetRawText());
            }
            
            _logger.LogWarning("VOM API response format unexpected or success=false: {Response}", responseContent);
            return default;
        }

        _logger.LogError("POST request to {Endpoint} failed. Status: {StatusCode}, Response: {Response}", endpoint, response.StatusCode, responseContent);
        return default;
    }

    public async Task<HttpResponseMessage> PostAsync(string endpoint, object data)
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
        }

        var jsonContent = JsonSerializer.Serialize(data);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
        request.Content = content;
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        return await _httpClient.SendAsync(request);
    }

    public async Task<List<VomUnit>?> GetAllUnitsAsync()
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/products/units");
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataElement) && dataElement.TryGetProperty("units", out var unitsElement))
            {
                if (unitsElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<VomUnit>>(unitsElement.GetRawText());
                }
                else if (unitsElement.ValueKind == JsonValueKind.Object)
                {
                    var singleUnit = JsonSerializer.Deserialize<VomUnit>(unitsElement.GetRawText());
                    return singleUnit != null ? new List<VomUnit> { singleUnit } : new List<VomUnit>();
                }
                else if (unitsElement.ValueKind == JsonValueKind.Null)
                {
                    return new List<VomUnit>();
                }
            }
            _logger.LogWarning("Unexpected JSON structure in response: {Content}", responseContent);
            return new List<VomUnit>();
        }

        _logger.LogError("GET request to /api/products/units failed. Status: {StatusCode}", response.StatusCode);
        return default;
    }

    public async Task<List<VomSupplier>?> GetAllSuppliersAsync()
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/purchases/suppliers");
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<VomSupplier>>(dataElement.GetRawText());
                }
                else if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("suppliers", out var suppliersElement))
                {
                    // Handle case where suppliers are nested under data.suppliers
                    return JsonSerializer.Deserialize<List<VomSupplier>>(suppliersElement.GetRawText());
                }
                else if (dataElement.ValueKind == JsonValueKind.Object)
                {
                    var singleSupplier = JsonSerializer.Deserialize<VomSupplier>(dataElement.GetRawText());
                    return singleSupplier != null ? new List<VomSupplier> { singleSupplier } : new List<VomSupplier>();
                }
                else if (dataElement.ValueKind == JsonValueKind.Null)
                {
                    return new List<VomSupplier>();
                }
            }
            _logger.LogWarning("Unexpected JSON structure in suppliers response: {Content}", responseContent);
            return new List<VomSupplier>();
        }

        _logger.LogError("GET request to /api/purchases/suppliers failed. Status: {StatusCode}", response.StatusCode);
        return default;
    }

    public async Task<List<VomCategory>?> GetAllCategoriesAsync()
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/products/categories");
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<VomCategory>>(dataElement.GetRawText());
                }
                else if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("categories", out var categoriesElement))
                {
                    // Handle case where categories are nested under data.categories
                    return JsonSerializer.Deserialize<List<VomCategory>>(categoriesElement.GetRawText());
                }
                else if (dataElement.ValueKind == JsonValueKind.Object)
                {
                    var singleCategory = JsonSerializer.Deserialize<VomCategory>(dataElement.GetRawText());
                    return singleCategory != null ? new List<VomCategory> { singleCategory } : new List<VomCategory>();
                }
                else if (dataElement.ValueKind == JsonValueKind.Null)
                {
                    return new List<VomCategory>();
                }
            }
            _logger.LogWarning("Unexpected JSON structure in categories response: {Content}", responseContent);
            return new List<VomCategory>();
        }

        _logger.LogError("GET request to /api/products/categories failed. Status: {StatusCode}", response.StatusCode);
        return default;
    }

    public Task<List<VomProduct>?> GetAllProductsAsync()
    {
        // Based on testing, the VOM /api/products/products endpoint returns metadata (categories, warehouses, etc.)
        // but does not include an actual products array. The VOM API appears to require searching for products
        // individually by name rather than providing a bulk list endpoint.

        _logger.LogInformation("VOM API does not provide a bulk product list endpoint. Products must be searched individually during sync.");
        return Task.FromResult<List<VomProduct>?>(new List<VomProduct>());
    }

    public async Task<VomProduct?> SearchProductByNameAsync(string productName)
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        // Try different search approaches
        string[] searchEndpoints = {
            $"/api/products/search?name={Uri.EscapeDataString(productName)}",
            $"/api/products?search={Uri.EscapeDataString(productName)}",
            $"/api/products?name={Uri.EscapeDataString(productName)}",
            $"/api/inventory/products?search={Uri.EscapeDataString(productName)}"
        };

        foreach (var endpoint in searchEndpoints)
        {
            try
            {
                _logger.LogInformation("Searching for product '{ProductName}' using endpoint: {Endpoint}", productName, endpoint);

                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{endpoint}");
                AddCommonHeaders(request);
                request.Headers.Add("Authorization", $"Bearer {token}");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Search response from {Endpoint}: Status={Status}", endpoint, response.StatusCode);

                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(responseContent))
                {
                    using var doc = JsonDocument.Parse(responseContent);
                    var root = doc.RootElement;

                    // Try to parse different response structures
                    if (root.TryGetProperty("data", out var dataElement))
                    {
                        // Check for products array
                        if (dataElement.TryGetProperty("products", out var productsElement) && productsElement.ValueKind == JsonValueKind.Array)
                        {
                            var products = JsonSerializer.Deserialize<List<VomProduct>>(productsElement.GetRawText());
                            var matchingProduct = products?.FirstOrDefault(p =>
                                string.Equals(p.name_en, productName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.name_ar, productName, StringComparison.OrdinalIgnoreCase));

                            if (matchingProduct != null)
                            {
                                _logger.LogInformation("Found matching product '{ProductName}' with ID: {VomProductId}", productName, matchingProduct.id);
                                return matchingProduct;
                            }
                        }
                        else if (dataElement.ValueKind == JsonValueKind.Array)
                        {
                            var products = JsonSerializer.Deserialize<List<VomProduct>>(dataElement.GetRawText());
                            var matchingProduct = products?.FirstOrDefault(p =>
                                string.Equals(p.name_en, productName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.name_ar, productName, StringComparison.OrdinalIgnoreCase));

                            if (matchingProduct != null)
                            {
                                _logger.LogInformation("Found matching product '{ProductName}' with ID: {VomProductId}", productName, matchingProduct.id);
                                return matchingProduct;
                            }
                        }
                        else if (dataElement.ValueKind == JsonValueKind.Object)
                        {
                            // Single product response
                            var product = JsonSerializer.Deserialize<VomProduct>(dataElement.GetRawText());
                            if (product != null && (
                                string.Equals(product.name_en, productName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(product.name_ar, productName, StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("Found matching product '{ProductName}' with ID: {VomProductId}", productName, product.id);
                                return product;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to search for product '{ProductName}' at {Endpoint}: {Error}", productName, endpoint, ex.Message);
                continue;
            }
        }

        _logger.LogWarning("Could not find product '{ProductName}' through search", productName);
        return null;
    }

    public async Task<List<VomBill>?> GetAllPurchaseBillsAsync()
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/purchases/purchase-bills");
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<VomBill>>(dataElement.GetRawText());
                }
                else if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("purchase_bills", out var billsElement))
                {
                    // Handle case where bills are nested under data.purchase_bills
                    return JsonSerializer.Deserialize<List<VomBill>>(billsElement.GetRawText());
                }
                else if (dataElement.ValueKind == JsonValueKind.Object)
                {
                    var singleBill = JsonSerializer.Deserialize<VomBill>(dataElement.GetRawText());
                    return singleBill != null ? new List<VomBill> { singleBill } : new List<VomBill>();
                }
                else if (dataElement.ValueKind == JsonValueKind.Null)
                {
                    return new List<VomBill>();
                }
            }
            _logger.LogWarning("Unexpected JSON structure in purchase bills response: {Content}", responseContent);
            return new List<VomBill>();
        }

        _logger.LogError("GET request to /api/purchases/purchase-bills failed. Status: {StatusCode}", response.StatusCode);
        return default;
    }

    public async Task<VomBill?> CreatePurchaseBillAsync(object billData)
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("Cannot create purchase bill - no valid token");
            return default;
        }

        var jsonContent = JsonSerializer.Serialize(billData);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogInformation("Creating purchase bill with data: {Data}", jsonContent);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/purchases/purchase-bills");
        request.Content = content;
        AddCommonHeaders(request);
        var authHeader = $"Bearer {token}";
        request.Headers.Add("Authorization", authHeader);

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("POST request to create purchase bill succeeded. Response: {Response}", responseContent);

            // Parse the VOM API response structure: { "status": 200, "data": {...}, "success": true }
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var successElement) && successElement.GetBoolean() &&
                root.TryGetProperty("data", out var dataElement))
            {
                // Handle purchase bill creation response which may have nested "purchase_bill" object
                if (dataElement.TryGetProperty("purchase_bill", out var billElement))
                {
                    _logger.LogInformation("Purchase bill creation successful, parsing bill from response");
                    return JsonSerializer.Deserialize<VomBill>(billElement.GetRawText());
                }
                // Handle regular responses
                return JsonSerializer.Deserialize<VomBill>(dataElement.GetRawText());
            }

            _logger.LogWarning("VOM API response format unexpected or success=false: {Response}", responseContent);
            return default;
        }

        _logger.LogError("POST request to create purchase bill failed. Status: {StatusCode}, Full Response Body: {Response}", response.StatusCode, responseContent);

        // Also log as warning to make it more visible
        _logger.LogWarning("VOM API Purchase Bill Creation Error - Status: {StatusCode} | Full Response: {Response}", response.StatusCode, responseContent);
        return default;
    }

    public async Task<VomBill?> UpdatePurchaseBillAsync(int billId, object billData)
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("Cannot update purchase bill - no valid token");
            return default;
        }

        var jsonContent = JsonSerializer.Serialize(billData);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogInformation("Updating purchase bill {BillId} with data: {Data}", billId, jsonContent);

        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/api/purchases/purchase-bills/{billId}");
        request.Content = content;
        AddCommonHeaders(request);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("PUT request to update purchase bill succeeded. Response: {Response}", responseContent);

            // Parse the VOM API response structure: { "status": 200, "data": {...}, "success": true }
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var successElement) && successElement.GetBoolean() &&
                root.TryGetProperty("data", out var dataElement))
            {
                // Handle purchase bill update response which may have nested "purchase_bill" object
                if (dataElement.TryGetProperty("purchase_bill", out var billElement))
                {
                    _logger.LogInformation("Purchase bill update successful, parsing bill from response");
                    return JsonSerializer.Deserialize<VomBill>(billElement.GetRawText());
                }
                // Handle regular responses
                return JsonSerializer.Deserialize<VomBill>(dataElement.GetRawText());
            }

            _logger.LogWarning("VOM API response format unexpected or success=false: {Response}", responseContent);
            return default;
        }

        _logger.LogError("PUT request to update purchase bill failed. Status: {StatusCode}, Response: {Response}", response.StatusCode, responseContent);
        return default;
    }

    private void AddCommonHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("Api-Agent", ApiAgent);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept-Language", AcceptLanguage);
    }

    private class LoginResponse
    {
        public Data? data { get; set; }
    }

    private class Data
    {
        public string? token { get; set; }
    }

    private class VomLoginResponse
    {
        public int status { get; set; }
        public VomLoginData? data { get; set; }
        public bool success { get; set; }
        public object? errors { get; set; }
    }

    private class VomLoginData
    {
        public string? token { get; set; }
        public VomUser? user { get; set; }
        public VomCompanyInfo? companyInfo { get; set; }
    }

    private class VomUser
    {
        public int id { get; set; }
        public string? uname { get; set; }
        public string? email { get; set; }
        public string? country_code { get; set; }
        public string? mobile { get; set; }
    }

    private class VomCompanyInfo
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? fqdn { get; set; }
    }

    private class SessionResponse
    {
        public SessionUser? User { get; set; }
        public List<SessionLocation>? Locations { get; set; }
        public int Status { get; set; }
        public string? Description { get; set; }
    }

    private class SessionUser
    {
        public int SubUserID { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? CompanyCode { get; set; }
        public int LocationID { get; set; }
        public List<LoginSession>? LoginSessions { get; set; }
    }

    private class SessionLocation
    {
        public int LocationID { get; set; }
        public string? Name { get; set; }
        public string? Descripiton { get; set; }
        public string? Address { get; set; }
        public string? ContactNo { get; set; }
        public string? Email { get; set; }
        public string? Currency { get; set; }
        public int UserID { get; set; }
        public int StatusID { get; set; }
    }

    private class LoginSession
    {
        public int LocationID { get; set; }
        public string? Session { get; set; }
        public string? LocationName { get; set; }
    }
}