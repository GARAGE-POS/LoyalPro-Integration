using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Karage.Functions.Data;
using Karage.Functions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


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

// Add HttpClient factory
builder.Services.AddHttpClient();

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
