using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Karage.Functions;

public class KarageOpenApiConfigurationOptions : DefaultOpenApiConfigurationOptions
{
    public override OpenApiInfo Info { get; set; } = new OpenApiInfo
    {
        Version = "1.0.0",
        Title = "Karage Functions API",
        Description = "Azure Functions API for ERP integrations including VOM, Tamara, Sadeq, and OTP services. Each endpoint has its own authentication method - refer to individual endpoint documentation.",
        Contact = new OpenApiContact
        {
            Name = "Karage Support",
            Email = "support@karage.com"
        }
    };

    public override OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V2;
}
