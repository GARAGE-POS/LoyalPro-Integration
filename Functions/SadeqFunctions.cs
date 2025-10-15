using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text;
using System.Text.Json;
using Karage.Functions.Data;
using Karage.Functions.Models;

namespace Karage.Functions.Functions;

public class SadeqFunctions
{
    private readonly ILogger<SadeqFunctions> _logger;
    private readonly HttpClient _httpClient;
    private readonly V1DbContext _dbContext;
    
    // Environment variables
    private readonly string _sadeqUrl;
    private readonly string _sadeqUsername;
    private readonly string _sadeqPassword;
    private readonly string _sadeqAccountId;
    private readonly string _sadeqAccountSecret;
    private readonly string _sadeqRequestUsername;
    private readonly string _sadeqRequestPassword;

    public SadeqFunctions(ILogger<SadeqFunctions> logger, HttpClient httpClient, V1DbContext dbContext)
    {
        _logger = logger;
        _httpClient = httpClient;
        _dbContext = dbContext;
        
        // Load environment variables
        _sadeqUrl = Environment.GetEnvironmentVariable("SADQ_URL") ?? "";
        _sadeqUsername = Environment.GetEnvironmentVariable("SADQ_USERNAME") ?? "";
        _sadeqPassword = Environment.GetEnvironmentVariable("SADQ_PASSWORD") ?? "";
        _sadeqAccountId = Environment.GetEnvironmentVariable("SADQ_ACCOUNT_ID") ?? "";
        _sadeqAccountSecret = Environment.GetEnvironmentVariable("SADQ_ACCOUNT_SECRET") ?? "";
        _sadeqRequestUsername = Environment.GetEnvironmentVariable("SADQ_REQUEST_USERNAME") ?? "";
        _sadeqRequestPassword = Environment.GetEnvironmentVariable("SADQ_REQUEST_PASSWORD") ?? "";
    }

    [Function("sadeq_request")]
    [OpenApiOperation(operationId: "SadeqRequest", tags: new[] { "Sadeq" }, Summary = "Create Sadeq digital signature request", Description = "Initiates a digital signature envelope and sends invitation to specified recipient")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SadeqRequest), Required = true, Description = "Sadeq signature request details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Signature request created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Missing required fields")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.InternalServerError, Description = "Internal server error")]
    public async Task<IActionResult> SadeqRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sadeq_request")] HttpRequest req)
    {
        _logger.LogInformation("Sadeq request endpoint called.");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid JSON body.");
            }

            var root = doc.RootElement;

            // Check required fields
            var requiredFields = new[] { "companyCode", "destinationName", "destinationEmail", "destinationPhoneNumber", "nationalId", "templateId" };
            var missingFields = new List<string>();

            foreach (var field in requiredFields)
            {
                if (!root.TryGetProperty(field, out var element) || string.IsNullOrEmpty(element.GetString()))
                {
                    missingFields.Add(field);
                }
            }

            if (missingFields.Any())
            {
                return new BadRequestObjectResult($"Missing required fields: {string.Join(", ", missingFields)}");
            }

            // Extract values
            var companyCode = root.GetProperty("companyCode").GetString()!;
            var destinationName = root.GetProperty("destinationName").GetString()!;
            var destinationEmail = root.GetProperty("destinationEmail").GetString()!;
            var destinationPhoneNumber = root.GetProperty("destinationPhoneNumber").GetString()!;
            var nationalId = root.GetProperty("nationalId").GetString()!;
            var templateId = root.GetProperty("templateId").GetString()!;

            // Execute the workflow: get token, initiate envelope, send invitation
            var accessToken = await GetSadeqToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                return new ObjectResult("Failed to obtain access token.") { StatusCode = 500 };
            }

            var documentId = await InitiateEnvelopeByTemplate(accessToken, templateId);
            if (string.IsNullOrEmpty(documentId))
            {
                return new ObjectResult("Failed to obtain document ID.") { StatusCode = 500 };
            }

            var url = $"{_sadeqUrl}/IntegrationService/Invitation/Send-Invitation";
            var payload = new
            {
                documentId = documentId,
                destinations = new[]
                {
                    new
                    {
                        destinationName = destinationName,
                        destinationEmail = destinationEmail,
                        destinationPhoneNumber = destinationPhoneNumber,
                        nationalId = nationalId,
                        signeOrder = 0,
                        ConsentOnly = true,
                        signatories = new object[] { },
                        availableTo = GetDate30DaysFromNow(),
                        allowUserToSignAnyWhere = false,
                        authenticationType = 1,
                        invitationLanguage = 1,
                        redirectUrl = "",
                        allowUserToAddDestination = false
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Save contract status to database
            string? envelopId = null;
            try
            {
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                if (jsonResponse.TryGetProperty("data", out var dataElement))
                {
                    if (dataElement.TryGetProperty("envelopId", out var envelopIdElement))
                    {
                        envelopId = envelopIdElement.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse envelopId from response");
            }

            var contractStatus = new ContractStatus
            {
                CompanyCode = companyCode,
                CompanyName = destinationName,
                PhoneNumber = destinationPhoneNumber,
                Terminals = root.TryGetProperty("terminals", out var terminalsElement) ? terminalsElement.GetString() : null,
                NationalId = nationalId,
                Email = destinationEmail,
                TemplateId = templateId,
                DocumentId = documentId,
                EnvelopId = envelopId,
                SadqSent = response.IsSuccessStatusCode,
                Signed = false,
                ErrorMessage = response.IsSuccessStatusCode ? null : $"API returned {response.StatusCode}",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.IntegrationSadeqContracts.Add(contractStatus);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Contract status saved to database for {CompanyName} with DocumentId: {DocumentId}", 
                destinationName, documentId);

            return new ContentResult
            {
                Content = responseContent,
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sadeq request");
            _logger.LogTrace(ex.StackTrace);
            _logger.LogInformation("Sadeq Request Username: {Username}", _sadeqRequestUsername);
            return new StatusCodeResult(500);
        }
    }

    private async Task<string?> GetSadeqToken()
    {
        var url = $"{_sadeqUrl}/Authentication/Authority/Token";
        
        // Check if critical variables are missing
        if (string.IsNullOrEmpty(_sadeqRequestUsername) || string.IsNullOrEmpty(_sadeqRequestPassword))
        {
            _logger.LogError("Missing critical environment variables: REQUEST_USERNAME={RequestUsername}, REQUEST_PASSWORD={PasswordSet}", 
                _sadeqRequestUsername, string.IsNullOrEmpty(_sadeqRequestPassword) ? "None" : "set");
            return null;
        }

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_sadeqUsername}:{_sadeqPassword}"));
        var payload = $"grant_type=integration&password={_sadeqRequestPassword}&username={_sadeqRequestUsername}&accountId={_sadeqAccountId}&accountSecret={_sadeqAccountSecret}";
        
        var content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {basicAuth}");

        var response = await _httpClient.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        try
        {
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
            if (jsonResponse.TryGetProperty("access_token", out var tokenElement))
            {
                return tokenElement.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing token response");
        }

        return null;
    }

    private async Task<string?> InitiateEnvelopeByTemplate(string accessToken, string templateId)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogError("Failed to obtain access token.");
            return null;
        }

        var url = $"{_sadeqUrl}/IntegrationService/Document/Initiate-envelope-by-template";
        
        var formData = new MultipartFormDataContent();
        formData.Add(new StringContent(templateId), "TemplateId");
        formData.Add(new StringContent("false"), "UserOnlySigner");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("accept", "text/plain");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.PostAsync(url, formData);
        var responseContent = await response.Content.ReadAsStringAsync();

        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            // Extract documentId from the nested data structure
            if (result.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.TryGetProperty("documentId", out var documentIdElement))
                {
                    return documentIdElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing envelope response");
        }

        return null;
    }

    private string GetDate30DaysFromNow()
    {
        var futureDate = DateTime.Now.AddDays(30);
        return futureDate.ToString("yyyy-MM-dd");
    }

    [Function("get_contracts")]
    [OpenApiOperation(operationId: "GetContracts", tags: new[] { "Sadeq" }, Summary = "Get all contracts", Description = "Retrieves all contracts from the database with their current status")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "List of all contracts")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.InternalServerError, Description = "Internal server error")]
    public async Task<IActionResult> GetContracts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "contracts")] HttpRequest req)
    {
        _logger.LogInformation("Get contracts endpoint called.");

        try
        {
            var contracts = await _dbContext.IntegrationSadeqContracts
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return new OkObjectResult(new
            {
                success = true,
                count = contracts.Count,
                data = contracts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving contracts");
            return new ObjectResult(new { success = false, error = "Internal server error" }) { StatusCode = 500 };
        }
    }

    [Function("sadeq_upload_pdf")]
    [OpenApiOperation(operationId: "SadeqUploadPdf", tags: new[] { "Sadeq" }, Summary = "Upload PDF and create Sadeq envelope", Description = "Converts PDF to base64 and initiates an envelope with the Sadeq API. Requires customer information in form fields.")]
    [OpenApiRequestBody(contentType: "multipart/form-data", bodyType: typeof(SadeqPdfUploadRequest), Required = true, Description = "PDF file and customer information")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Envelope created successfully with document ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Missing or invalid PDF file or customer information")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.InternalServerError, Description = "Internal server error")]
    public async Task<IActionResult> SadeqUploadPdf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sadeq_upload_pdf")] HttpRequest req)
    {
        _logger.LogInformation("Sadeq PDF upload endpoint called.");

        try
        {
            // Check if request has files
            if (!req.HasFormContentType || req.Form.Files.Count == 0)
            {
                return new BadRequestObjectResult(new { error = "No PDF file provided. Please upload a file." });
            }

            // Extract form fields
            var companyCode = req.Form["companyCode"].ToString();
            var companyName = req.Form["companyName"].ToString();
            var phoneNumber = req.Form["phoneNumber"].ToString();
            var email = req.Form["email"].ToString();
            var nationalId = req.Form["nationalId"].ToString();
            var terminals = req.Form["terminals"].ToString();

            // Validate required fields
            var missingFields = new List<string>();
            if (string.IsNullOrEmpty(companyCode)) missingFields.Add("companyCode");
            if (string.IsNullOrEmpty(companyName)) missingFields.Add("companyName");
            if (string.IsNullOrEmpty(phoneNumber)) missingFields.Add("phoneNumber");
            if (string.IsNullOrEmpty(email)) missingFields.Add("email");
            if (string.IsNullOrEmpty(nationalId)) missingFields.Add("nationalId");

            if (missingFields.Any())
            {
                return new BadRequestObjectResult(new { error = $"Missing required fields: {string.Join(", ", missingFields)}" });
            }

            var pdfFile = req.Form.Files[0];

            // Validate file extension
            if (!pdfFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = "Invalid file type. Only PDF files are accepted." });
            }

            // Read file content and convert to base64
            string base64Content;
            using (var memoryStream = new MemoryStream())
            {
                await pdfFile.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();
                base64Content = Convert.ToBase64String(fileBytes);
            }

            _logger.LogInformation("PDF file '{FileName}' converted to base64. Size: {Size} bytes", pdfFile.FileName, pdfFile.Length);

            // Get access token
            var accessToken = await GetSadeqToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                return new ObjectResult(new { error = "Failed to obtain access token from Sadeq API." }) { StatusCode = 500 };
            }

            // Prepare the request to initiate envelope with base64
            var url = $"{_sadeqUrl}/IntegrationService/Document/Initiate-envelope-Base64";
            
            var payload = new
            {
                UserOnlySigner = false,
                File = new
                {
                    FileName = pdfFile.FileName,
                    File = base64Content
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Sadeq API returned error. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseContent);
            }

            // Parse response to extract documentId and envelopId
            string? documentId = null;
            string? envelopId = null;
            try
            {
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                if (jsonResponse.TryGetProperty("data", out var dataElement))
                {
                    if (dataElement.TryGetProperty("documentId", out var docIdElement))
                    {
                        documentId = docIdElement.GetString();
                    }
                    if (dataElement.TryGetProperty("envelopId", out var envIdElement))
                    {
                        envelopId = envIdElement.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse documentId/envelopId from response");
            }

            // Save contract status to database
            var contractStatus = new ContractStatus
            {
                CompanyCode = companyCode,
                CompanyName = companyName,
                PhoneNumber = phoneNumber,
                Terminals = string.IsNullOrEmpty(terminals) ? null : terminals,
                NationalId = nationalId,
                Email = email,
                PdfFileName = pdfFile.FileName,
                DocumentId = documentId,
                EnvelopId = envelopId,
                SadqSent = response.IsSuccessStatusCode,
                Signed = false,
                ErrorMessage = response.IsSuccessStatusCode ? null : $"API returned {response.StatusCode}",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.IntegrationSadeqContracts.Add(contractStatus);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Contract status saved to database for {CompanyName} with PDF: {FileName}, DocumentId: {DocumentId}", 
                companyName, pdfFile.FileName, documentId);

            return new ContentResult
            {
                Content = responseContent,
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sadeq PDF upload");
            return new ObjectResult(new { error = "Internal server error", details = ex.Message }) { StatusCode = 500 };
        }
    }
}

// OpenAPI Models for Sadeq functions
public class SadeqRequest
{
    public string companyCode { get; set; } = string.Empty;
    public string destinationName { get; set; } = string.Empty;
    public string destinationEmail { get; set; } = string.Empty;
    public string destinationPhoneNumber { get; set; } = string.Empty;
    public string nationalId { get; set; } = string.Empty;
    public string templateId { get; set; } = string.Empty;
    public string? terminals { get; set; }
}

public class SadeqPdfUploadRequest
{
    public IFormFile? File { get; set; }
    public string companyCode { get; set; } = string.Empty;
    public string companyName { get; set; } = string.Empty;
    public string phoneNumber { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string nationalId { get; set; } = string.Empty;
    public string? terminals { get; set; }
}