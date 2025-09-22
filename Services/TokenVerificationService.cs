using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Karage.Functions.Services;

public class TokenVerificationService
{
    private static readonly string Secret = "GARA01OLKvCahKKi/XmpCBGH9aZXpasTuMyal3f3tKRIbJasLhz4SzYh4NBVCXi3KJHlSXKP+oi2+bXr6CUYTR==";

    public static bool VerifyToken(string authorizationHeader, out string? customerId)
    {
        customerId = null;

        if (string.IsNullOrEmpty(authorizationHeader))
            return false;

        if (!authorizationHeader.StartsWith("Bearer "))
            return false;

        string token = authorizationHeader.Substring(7);

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Convert.FromBase64String(Secret);

            var validationParameters = new TokenValidationParameters
            {
                RequireExpirationTime = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            ClaimsPrincipal principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            var customerIdClaim = principal.FindFirst("customerID");
            if (customerIdClaim != null)
            {
                customerId = customerIdClaim.Value;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}