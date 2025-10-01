using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace Karage.Functions.Functions;

public class OrderPayloadResponseDto
{
    public EventDto Event { get; set; } = new EventDto();
    public string POSBusinessReference { get; set; } = string.Empty;
    public int LocationID { get; set; }
    public CustomerDto Customer { get; set; } = new CustomerDto();
    public double? AmountTotal { get; set; }
    public double? DiscountedAmount { get; set; }
    public OrderDto Order { get; set; } = new OrderDto();
    public List<OrderItemDto> OrderItems { get; set; } = new List<OrderItemDto>();
    public int? OriginalOrderId { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Phone { get; set; } = string.Empty;
}

public class EventDto
{
    public string ID { get; set; } = Guid.NewGuid().ToString();
}

public class OrderDto
{
    public int OrderID { get; set; }
    public string OrderCreatedDT { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int? ItemID { get; set; }
    public string Name { get; set; } = string.Empty;
    public double? Price { get; set; }
    public double? Quantity { get; set; }
    public double? TotalPrice { get; set; }
}

public class OrderFunctions
{
    private readonly ILogger<OrderFunctions> _logger;
    private readonly V1DbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IApiKeyService _apiKeyService;

    public OrderFunctions(ILogger<OrderFunctions> logger, V1DbContext context, IConfiguration configuration, IApiKeyService apiKeyService)
    {
        _logger = logger;
        _context = context;
        _configuration = configuration;
        _apiKeyService = apiKeyService;
    }

    [Function("GetOrderPayload")]
    [OpenApiOperation(operationId: "GetOrderPayload", tags: new[] { "Orders" }, Summary = "Get order payload", Description = "Retrieves complete order information including items, customer, and amounts")]
    [OpenApiSecurity("X-API-Key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
    [OpenApiParameter(name: "orderId", In = ParameterLocation.Query, Required = true, Type = typeof(int), Description = "Order ID to retrieve")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(OrderPayloadResponseDto), Description = "Order retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Order not found")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid order ID")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Invalid or missing API key")]
    public async Task<IActionResult> GetOrderPayload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {

        //@TODO: Add refundorder flag by looking at OriginalOrderId
        _logger.LogInformation("Get order payload endpoint called.");

        var (verificationResult, user) = await _apiKeyService.VerifyApiKeyAndGetUser(req);
        if (verificationResult != null)
        {
            return verificationResult;
        }

        if (user == null)
        {
            return new UnauthorizedResult();
        }

        var orderIdStr = req.Query["orderId"].ToString();
        if (string.IsNullOrEmpty(orderIdStr) || !int.TryParse(orderIdStr, out int orderId))
        {
            return new BadRequestObjectResult(new { error = "Valid order ID is required" });
        }

        try
        {
            var orderData = await (from o in _context.Orders
                                 from oc in _context.OrderCheckouts.Where(oc => oc.OrderID == o.OrderID).DefaultIfEmpty()
                                 where o.OrderID == orderId
                                 select new
                                 {
                                     OrderID = o.OrderID,
                                     OrderCreatedDT = o.OrderCreatedDT,
                                     LocationID = o.LocationID,
                                     CustomerID = o.CustomerID,
                                     AmountTotal = oc.AmountTotal,
                                     AmountDiscount = oc.AmountDiscount,
                                     StatusID = o.StatusID
                                 }).FirstOrDefaultAsync();

            if (orderData == null)
            {
                return new NotFoundObjectResult(new { error = "Order not found" });
            }

            // Get all order items for this order
            // Only select columns that exist in OrderDetail table
            var orderDetails = await (from od in _context.OrderDetails
                                      where od.OrderID == orderId
                                      select new Karage.Functions.Models.OrderDetail {
                                          OrderDetailID = od.OrderDetailID,
                                          Cost = od.Cost,
                                          DiscountAmount = od.DiscountAmount,
                                          ItemID = od.ItemID,
                                          OrderID = od.OrderID,
                                          PackageID = od.PackageID,
                                          Price = od.Price,
                                          Quantity = od.Quantity,
                                          StatusID = od.StatusID,
                                          RefundQty = od.RefundQty,
                                          RefundAmount = od.RefundAmount
                                      }).ToListAsync();

            // Get all item IDs from order details
            var itemIds = orderDetails.Select(od => od.ItemID).Distinct().ToList();

            // Get mapping from ItemID+LocationID to UniqueItemID
            var uniqueItemMappings = await _context.MapUniqueItemIDs
                .Where(m => itemIds.Contains(m.ItemID) && m.LocationID == orderData.LocationID)
                .ToListAsync();

            // Get item names
            var itemsDict = await _context.Items
                .Where(i => itemIds.Contains(i.ItemID))
                .ToDictionaryAsync(i => i.ItemID, i => i.Name);

            // Build order items with UniqueItemID
            var orderItems = orderDetails.Select(od => {
                int itemId = od.ItemID ?? 0;
                var uniqueMap = uniqueItemMappings.FirstOrDefault(m => m.ItemID == itemId && m.LocationID == orderData.LocationID);
                double effectiveQuantity = 0;
                if (od.StatusID == 202)
                {
                    effectiveQuantity = (od.Quantity ?? 0) - (od.RefundQty ?? 0);
                }
                else if (od.StatusID == 204)
                {
                    effectiveQuantity = od.Quantity ?? 0;
                }
                return new OrderItemDto
                {
                    ItemID = uniqueMap?.UniqueItemID, // Use UniqueItemID instead of ItemID
                    Name = itemsDict.TryGetValue(itemId, out var name) ? name ?? "Unknown Item" : "Unknown Item",
                    Price = od.Price,
                    Quantity = effectiveQuantity,
                    TotalPrice = effectiveQuantity * (od.Price ?? 0)
                };
            }).ToList();

            // Fetch customer info
            Customer? customer = null;
            if (orderData.CustomerID.HasValue)
            {
                customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerID == orderData.CustomerID.Value);
            }

            var response = new OrderPayloadResponseDto
            {
                Event = new EventDto { ID = Guid.NewGuid().ToString() },
                POSBusinessReference = user.CompanyCode ?? string.Empty,
                LocationID = orderData.LocationID,
                Customer = new CustomerDto
                {
                    Id = orderData.CustomerID ?? 0,
                    Phone = customer?.Mobile ?? string.Empty
                },
                AmountTotal = orderData.AmountTotal,
                DiscountedAmount = orderData.AmountDiscount,
                Order = new OrderDto
                {
                    OrderID = orderData.OrderID,
                    OrderCreatedDT = orderData.OrderCreatedDT?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty
                },
                OrderItems = orderItems
            };

            // If the order itself has StatusID == 106 or 103, set OriginalOrderId in the response
            var orderStatus = orderData.StatusID;
            if (orderStatus.HasValue && (orderStatus.Value == 106 || orderStatus.Value == 103))
            {
                response.OriginalOrderId = orderData.OrderID;
            }

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving order payload for OrderId: {OrderId}", orderId);
            return new StatusCodeResult(500);
        }
    }
}
   
