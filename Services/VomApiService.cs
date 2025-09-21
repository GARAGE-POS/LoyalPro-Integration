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

// VOM Product models
public class VomProduct
{
    public int id { get; set; }
    public string? name_en { get; set; }
    public string? name_ar { get; set; }
    public string? description { get; set; }
    public decimal? buying_price { get; set; }
    public decimal? selling_price { get; set; }
    public int? category_id { get; set; }
    public int? unit_id { get; set; }
    public string? barcode { get; set; }
    public string? type { get; set; }
    public int? warehouse_id { get; set; }
    public decimal? quantity { get; set; }
    public bool? is_active { get; set; }
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public class VomProductsResponse
{
    public List<VomProduct>? data { get; set; }
}

public class VomApiService : IVomApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VomApiService> _logger;
    private const string BaseUrl = "https://nouravom.getvom.com";
    private const string ApiAgent = "zapier";
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

        // Use hardcoded session endpoint for authentication
        var sessionEndpoint = "http://api-uat.garage.sa/api/login/signin/2890/POS-KARAGE";

        var request = new HttpRequestMessage(HttpMethod.Get, sessionEndpoint);

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Session API response: {Response}", responseContent);

            var sessionResponse = JsonSerializer.Deserialize<SessionResponse>(responseContent);

            if (sessionResponse?.Status == 1 && sessionResponse?.User?.LoginSessions?.Count > 0)
            {
                var firstSession = sessionResponse.User.LoginSessions[0];
                _cachedToken = firstSession.Session;
                // Set token to expire in 1 hour (adjust based on actual token expiry)
                _tokenExpiry = DateTime.UtcNow.AddHours(1);
                _logger.LogInformation("Successfully obtained session token: {TokenStart}...", _cachedToken?.Substring(0, Math.Min(10, _cachedToken.Length)));
                return _cachedToken;
            }
        }

        _logger.LogError("Failed to authenticate with session API. Status: {StatusCode}", response.StatusCode);
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
            
            if (root.TryGetProperty("data", out var dataElement) && root.TryGetProperty("success", out var successElement) && successElement.GetBoolean())
            {
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

    public async Task<List<VomProduct>?> GetAllProductsAsync()
    {
        var token = await GetAuthToken();
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        // Note: VOM API /api/products/products returns metadata, not actual products
        // For now, we'll return an empty list and focus on creating products
        // This method may need to be updated once we find the correct products listing endpoint
        _logger.LogWarning("VOM products listing endpoint not yet discovered. Returning empty list for matching purposes.");
        return new List<VomProduct>();
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