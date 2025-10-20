using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Models;

namespace OrderService.Services;

public class InventoryHttpClient : IInventoryClient
{
    private readonly HttpClient _client;
    private readonly ILogger<InventoryHttpClient> _logger;
    private readonly InventoryClientOptions _options;

    public InventoryHttpClient(HttpClient client, IOptions<InventoryClientOptions> options, ILogger<InventoryHttpClient> logger)
    {
        _client = client;
        _logger = logger;
        _options = options.Value;
    }

    public async Task NotifyOrderAcceptedAsync(OrderRecord order, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(_options.ReservePath, new
            {
                orderId = order.OrderId,
                customerId = order.CustomerId,
                itemSkus = order.ItemSkus,
                totalAmount = order.TotalAmount
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Inventory reserve call returned status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inventory reserve call failed");
        }
    }
}

public class InventoryClientOptions
{
    public string ReservePath { get; set; } = "/api/inventory/reserve";
}
