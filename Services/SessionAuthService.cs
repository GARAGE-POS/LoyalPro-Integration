using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Data;
using Karage.Functions.Models;
using Microsoft.Extensions.Logging;

namespace Karage.Functions.Services;

public interface ISessionAuthService
{
    Task<(IActionResult? result, SessionData? sessionData)> VerifySessionAndGetData(HttpRequest req);
}

public class SessionAuthService : ISessionAuthService
{
    private readonly V1DbContext _context;
    private readonly ILogger<SessionAuthService> _logger;

    public SessionAuthService(V1DbContext context, ILogger<SessionAuthService> logger)
    {
        _context = context;
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
            _logger.LogWarning("Empty session token");
            return (new UnauthorizedObjectResult(new
            {
                Status = 401,
                Description = "Session token is required"
            }), null);
        }

        // Check session using local database method
        var sessionData = await CheckSessionV2(authenticationSession);
        if (sessionData == null)
        {
            _logger.LogWarning("Invalid session: {Session}", authenticationSession);
            return (new UnauthorizedObjectResult(new
            {
                Status = 401,
                Description = "Invalid session"
            }), null);
        }

        _logger.LogInformation("Session validated successfully. User: {UserId}, Location: {LocationId}",
            sessionData.UserID, sessionData.LocationID);

        return (null, sessionData);
    }

    private async Task<SessionData?> CheckSessionV2(string authenticationSession)
    {
        try
        {
            // This method should query your session/login table directly from the database
            // Based on your session token, find the corresponding user and location

            // For now, I'll extract company code and find user, but you should replace this
            // with actual session table queries based on your database schema

            var companyCode = ExtractCompanyCodeFromSession(authenticationSession);
            if (string.IsNullOrEmpty(companyCode))
            {
                _logger.LogWarning("Could not extract company code from session: {Session}", authenticationSession);
                return null;
            }

            // Find user by company code
            var user = await _context.Users
                .Where(u => u.CompanyCode == companyCode && u.StatusID == 1)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning("User not found for company code: {CompanyCode}", companyCode);
                return null;
            }

            // TODO: Replace this with actual session validation from your session table
            // For now, creating mock session data for testing
            return new SessionData
            {
                UserID = user.UserID,
                LocationID = 1, // Default location ID - should come from session table
                Session = authenticationSession,
                LocationName = "Test Location",
                CompanyTitle = "Test Company",
                CompanyAddress = "Test Address",
                CompanyPhones = "123456789",
                CompanyEmail = "test@example.com",
                Currency = "SAR",
                CountryID = "1",
                VATNo = "123456789",
                Tax = "15.0"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session validation");
            return null;
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
}