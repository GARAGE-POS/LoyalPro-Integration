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

        // Token is expired or doesn't exist, get a new one
        var loginRequest = new
        {
            email = "Odai.alhasan88@gmail.com",
            password = "Aa1m7A5dMD5"
        };

        var json = JsonSerializer.Serialize(loginRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/companyuser/login");
        request.Content = content;
        AddCommonHeaders(request);

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent);
            
            if (loginResponse?.data?.token != null)
            {
                _cachedToken = loginResponse.data.token;
                // Set token to expire in 1 hour (adjust based on actual token expiry)
                _tokenExpiry = DateTime.UtcNow.AddHours(1);
                _logger.LogInformation("Successfully obtained and cached VOM API token: {TokenStart}...", _cachedToken.Substring(0, Math.Min(10, _cachedToken.Length)));
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
}