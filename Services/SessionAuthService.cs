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
            // Validate session using local database SessionInfo table
            var sessionInfo = await _context.SessionInfos
                .Include(s => s.SubUser)
                .Include(s => s.Location)
                .Where(s => s.SessionId == authenticationSession && s.StatusID == 1)
                .FirstOrDefaultAsync();

            if (sessionInfo == null)
            {
                _logger.LogWarning("Session not found in database: {Session}", authenticationSession);
                return null;
            }

            // Get user information from SubUser
            var subUser = sessionInfo.SubUser;
            if (subUser == null)
            {
                _logger.LogWarning("SubUser not found for session: {Session}, SubUserId: {SubUserId}",
                    authenticationSession, sessionInfo.SubUserId);
                return null;
            }

            // Get location information
            var location = sessionInfo.Location;
            if (location == null)
            {
                _logger.LogWarning("Location not found for session: {Session}, LocationId: {LocationId}",
                    authenticationSession, sessionInfo.LocationID);
                return null;
            }

            _logger.LogInformation("Session validated successfully from database. SubUser: {SubUserId}, Location: {LocationId}",
                sessionInfo.SubUserId, sessionInfo.LocationID);

            return new SessionData
            {
                UserID = subUser.UserID, // Using UserID from SubUsers table for supplier filtering
                LocationID = sessionInfo.LocationID,
                Session = sessionInfo.SessionId,
                LocationName = location.Name,
                CompanyTitle = location.Name, // Using location name as company title
                CompanyAddress = location.Address,
                CompanyPhones = location.ContactNo,
                CompanyEmail = location.Email,
                Currency = sessionInfo.Currency ?? "SAR",
                CountryID = location.CountryID,
                VATNo = location.VATNO,
                Tax = location.Tax?.ToString() ?? "15"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session validation");
            return null;
        }
    }

}