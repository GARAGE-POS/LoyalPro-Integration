using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Karage.Functions.Functions;

public class VerifyMerchantFunctions
{
    private readonly ILogger<VerifyMerchantFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IApiKeyService _apiKeyService;

    public VerifyMerchantFunctions(ILogger<VerifyMerchantFunctions> logger, V1DbContext context, IConfiguration configuration, IApiKeyService apiKeyService)
    {
        _logger = logger;
        _context = context;
        _configuration = configuration;
        _apiKeyService = apiKeyService;
    }

    [Function("VerifyMerchant")]
    public async Task<IActionResult> VerifyMerchant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "VerifyMerchant")] HttpRequest req)
    {
        _logger.LogInformation("VerifyMerchant endpoint called.");

        try
        {
            var (verificationResult, user) = await _apiKeyService.VerifyApiKeyAndGetUser(req);
            if (verificationResult != null)
            {
                return verificationResult;
            }

            return new OkObjectResult(new
            {
                Company = user!.Company,
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
