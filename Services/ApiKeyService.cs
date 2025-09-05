using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Karage.Functions.Data;
using Karage.Functions.Models;
using Microsoft.EntityFrameworkCore;

namespace Karage.Functions.Services;

public interface IApiKeyService
{
    Task<IActionResult?> VerifyApiKey(HttpRequest req);
    Task<(IActionResult? result, User? user)> VerifyApiKeyAndGetUser(HttpRequest req);
}

public class ApiKeyService : IApiKeyService
{
    private readonly V1DbContext _context;

    public ApiKeyService(V1DbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult?> VerifyApiKey(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("X-API-Key", out var providedApiKey))
        {
            return new UnauthorizedResult();
        }

        var apiKey = providedApiKey.ToString();
        if (string.IsNullOrEmpty(apiKey))
        {
            return new UnauthorizedResult();
        }

        var user = await _context.Users
            .Where(u => u.Password == apiKey && u.StatusID == 1)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return new UnauthorizedResult();
        }

        return null; // null means verification passed
    }

    public async Task<(IActionResult? result, User? user)> VerifyApiKeyAndGetUser(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("X-API-Key", out var providedApiKey))
        {
            return (new UnauthorizedResult(), null);
        }

        var apiKey = providedApiKey.ToString();
        if (string.IsNullOrEmpty(apiKey))
        {
            return (new UnauthorizedResult(), null);
        }

        var user = await _context.Users
            .Where(u => u.Password == apiKey && u.StatusID == 1)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return (new UnauthorizedResult(), null);
        }

        return (null, user); // null result means verification passed
    }
}
