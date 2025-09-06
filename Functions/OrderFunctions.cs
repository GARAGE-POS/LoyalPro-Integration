using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Karage.Functions.Data;
using Karage.Functions.Services;
using Microsoft.Extensions.Configuration;

namespace Karage.Functions.Functions;

public class OrderPayloadResponseDto
{
    public EventDto Event { get; set; } = new EventDto();
    public string POSBusinessReference { get; set; } = string.Empty;
    public int LocationID { get; set; }
    public double? AmountTotal { get; set; }
    public double? DiscountedAmount { get; set; }
    public OrderDto Order { get; set; } = new OrderDto();
    public List<OrderItemDto> OrderItems { get; set; } = new List<OrderItemDto>();
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
    public async Task<IActionResult> GetOrderPayload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
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
                                     AmountTotal = oc.AmountTotal,
                                     AmountDiscount = oc.AmountDiscount
                                 }).FirstOrDefaultAsync();

            if (orderData == null)
            {
                return new NotFoundObjectResult(new { error = "Order not found" });
            }

            var orderItems = await (from od in _context.OrderDetails
                                  from i in _context.Items.Where(i => i.ItemID == od.ItemID).DefaultIfEmpty()
                                  where od.OrderID == orderId
                                  select new OrderItemDto
                                  {
                                      ItemID = od.ItemID,
                                      Name = i.Name ?? "Unknown Item"
                                  }).ToListAsync();

            var response = new OrderPayloadResponseDto
            {
                Event = new EventDto { ID = Guid.NewGuid().ToString() },
                POSBusinessReference = user.CompanyCode ?? string.Empty,
                LocationID = orderData.LocationID,
                AmountTotal = orderData.AmountTotal,
                DiscountedAmount = orderData.AmountDiscount,
                Order = new OrderDto
                {
                    OrderID = orderData.OrderID,
                    OrderCreatedDT = orderData.OrderCreatedDT?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty
                },
                OrderItems = orderItems
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving order payload for OrderId: {OrderId}", orderId);
            return new StatusCodeResult(500);
        }
    }
}
   
