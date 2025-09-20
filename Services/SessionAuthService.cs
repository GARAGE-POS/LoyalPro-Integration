using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Data;
using Karage.Functions.Models;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Karage.Functions.Services;

public interface ISessionAuthService
{
    Task<(IActionResult? result, SessionData? sessionData)> VerifySessionAndGetData(HttpRequest req);
}

public class SessionAuthService : ISessionAuthService
{
    private readonly V1DbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SessionAuthService> _logger;

    public SessionAuthService(V1DbContext context, IHttpClientFactory httpClientFactory, ILogger<SessionAuthService> logger)
    {
        _context = context;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<(IActionResult? result, SessionData? sessionData)> VerifySessionAndGetData(HttpRequest req)
    {
        // Check for Authorization header
        if (!req.Headers.TryGetValue("Authorization", out var authHeaderValues) || authHeaderValues.Count == 0)
        {
            _logger.LogWarning("Missing Authorization header");
            return (new UnauthorizedObjectResult(new
            {
                Status = 401,
                Description = "Authorization header is required"
            }), null);
        }

        string authHeaderValue = authHeaderValues.ToString();
        string authenticationSession = string.Empty;

        // Parse Bearer token or session token
        if (authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            authenticationSession = authHeaderValue.Substring("Bearer ".Length).Trim();
        }
        else
        {
            authenticationSession = authHeaderValue.Trim();
        }

        if (string.IsNullOrEmpty(authenticationSession))
        {
            _logger.LogWarning("Empty authorization parameter");
            return (new UnauthorizedObjectResult(new
            {
                Status = 401,
                Description = "Invalid authorization parameter"
            }), null);
        }

        try
        {
            // Extract company code from session (format: POS-3d6kqv...)
            var companyCode = ExtractCompanyCodeFromSession(authenticationSession);
            if (string.IsNullOrEmpty(companyCode))
            {
                _logger.LogWarning("Could not extract company code from session: {Session}", authenticationSession);
                return (new UnauthorizedObjectResult(new
                {
                    Status = 401,
                    Description = "Invalid session format"
                }), null);
            }

            // Find user by company code
            var user = await _context.Users
                .Where(u => u.CompanyCode == companyCode && u.StatusID == 1)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning("User not found for company code: {CompanyCode}", companyCode);
                return (new UnauthorizedObjectResult(new
                {
                    Status = 401,
                    Description = "Invalid session - user not found"
                }), null);
            }

            // Call session validation API
            var sessionResponse = await ValidateSessionWithAPI(user.UserID, authenticationSession);
            if (sessionResponse == null)
            {
                _logger.LogWarning("Session validation failed for session: {Session}", authenticationSession);
                return (new UnauthorizedObjectResult(new
                {
                    Status = 401,
                    Description = "Invalid session"
                }), null);
            }

            // Extract session data from the response
            var sessionData = ExtractSessionDataFromResponse(sessionResponse, authenticationSession);
            if (sessionData == null)
            {
                _logger.LogWarning("Could not extract session data from API response");
                return (new UnauthorizedObjectResult(new
                {
                    Status = 401,
                    Description = "Invalid session data"
                }), null);
            }

            _logger.LogInformation("Session validation successful for user {UserId}, location {LocationId}",
                sessionData.UserID, sessionData.LocationID);

            return (null, sessionData); // null result means verification passed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session validation");
            return (new StatusCodeResult(500), null);
        }
    }

    private string? ExtractCompanyCodeFromSession(string session)
    {
        // Session format: POS-3d6kqv... or POS-3D6KQV...
        if (session.StartsWith("POS-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = session.Split('-');
            if (parts.Length >= 2)
            {
                return $"POS-{parts[1].Substring(0, Math.Min(6, parts[1].Length)).ToUpper()}";
            }
        }
        return null;
    }

    private async Task<SessionResponse?> ValidateSessionWithAPI(int userId, string session)
    {
        try
        {
            // Call the signin API endpoint (simulated based on your example)
            var url = $"https://api-uat.garage.sa/api/login/signin/{userId}/{session}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Session API validation failed with status: {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var sessionResponse = JsonSerializer.Deserialize<SessionResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return sessionResponse?.Status == 1 ? sessionResponse : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling session validation API");
            return null;
        }
    }

    private SessionData? ExtractSessionDataFromResponse(SessionResponse response, string originalSession)
    {
        if (response.User?.LoginSessions == null || !response.User.LoginSessions.Any())
        {
            return null;
        }

        // Find the matching session from the login sessions
        var matchingSession = response.User.LoginSessions
            .FirstOrDefault(s => s.Session?.Contains(originalSession.Split('-').LastOrDefault() ?? "") == true);

        if (matchingSession == null)
        {
            // If no exact match, use the first session (fallback)
            matchingSession = response.User.LoginSessions.First();
        }

        return new SessionData
        {
            LocationID = matchingSession.LocationID,
            UserID = response.User.SuperUserID,
            Session = matchingSession.Session,
            LocationName = matchingSession.LocationName,
            CompanyTitle = matchingSession.CompanyTitle,
            CompanyAddress = matchingSession.CompanyAddress,
            CompanyPhones = matchingSession.CompanyPhones,
            CompanyEmail = matchingSession.CompanyEmail,
            Currency = matchingSession.Currency,
            CountryID = matchingSession.CountryID,
            VATNo = matchingSession.VATNo,
            Tax = matchingSession.Tax
        };
    }
}