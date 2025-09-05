using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Karage.Functions.Functions;

public class VerifyMerchantFunctions
{
    private readonly ILogger<VerifyMerchantFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IConfiguration _configuration;

    public VerifyMerchantFunctions(ILogger<VerifyMerchantFunctions> logger, V1DbContext context, IConfiguration configuration)
    {
        _logger = logger;
        _context = context;
        _configuration = configuration;
    }

    [Function("VerifyMerchant")]
    public async Task<IActionResult> VerifyMerchant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "VerifyMerchant")] HttpRequest req)
    {
        _logger.LogInformation("VerifyMerchant endpoint called.");

        try
        {
            if (!req.Headers.TryGetValue("X-API-Key", out var providedApiKey))
            {
                return new UnauthorizedResult();
            }

            var providedApiKeyString = providedApiKey.ToString();

            
            var user = await _context.Users
                .Where(u => u.Password == providedApiKeyString && u.StatusID == 1)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return new UnauthorizedResult();                
            }
          
            return new OkObjectResult(new
            {
                Company = user.Company,
                CompanyCode = user.CompanyCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in VerifyMerchant");
            return new StatusCodeResult(500);
        }
    }
}
