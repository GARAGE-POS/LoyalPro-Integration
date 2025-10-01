using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Karage.Functions.Data;
using Karage.Functions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Karage.Functions;


var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var connectionString = builder.Configuration["V1DatabaseConnectionString"];
Console.WriteLine($"Connection string value: '{connectionString}'");
if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("Connection string 'V1DatabaseConnectionString' not found.");
    Environment.Exit(1);
}

try
{
    builder.Services.AddDbContext<V1DbContext>(options =>
        options.UseSqlServer(connectionString));
}
catch (Exception ex)
{
    Console.WriteLine($"Error configuring DbContext: {ex.Message}");
    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
    Environment.Exit(1);
}

// Register API Key Service
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();

// Register VOM API Service
builder.Services.AddScoped<IVomApiService, VomApiService>();

// Register Session Authentication Service
builder.Services.AddScoped<ISessionAuthService, SessionAuthService>();

// Add HttpClient factory with SSL bypass for session endpoint
builder.Services.AddHttpClient<VomApiService>(client =>
{
    // Configure client defaults if needed
}).ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    // Skip SSL verification for the session endpoint (HTTP)
    handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
    return handler;
});

// Add default HttpClient for other services
builder.Services.AddHttpClient();

// Configure OpenAPI
builder.Services.AddSingleton<IOpenApiConfigurationOptions, KarageOpenApiConfigurationOptions>();

// Add global exception handler
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

var app = builder.Build();

// Wrap Run in try-catch for unhandled exceptions
try
{
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled exception: {ex.Message}");
    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
    throw;
}
