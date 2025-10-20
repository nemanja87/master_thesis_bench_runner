using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using OrderService.Models;

namespace OrderService.Services;

public class OrderApplicationService
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryClient _inventoryClient;
    private readonly ILogger<OrderApplicationService> _logger;

    public OrderApplicationService(
        IOrderRepository repository,
        IInventoryClient inventoryClient,
        ILogger<OrderApplicationService> logger)
    {
        _repository = repository;
        _inventoryClient = inventoryClient;
        _logger = logger;
    }

    public async Task<OrderRecord> CreateOrderAsync(OrderCreateRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var order = new OrderRecord
        {
            OrderId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            CustomerId = request.CustomerId,
            ItemSkus = request.ItemSkus.ToArray(),
            TotalAmount = request.TotalAmount,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(order, cancellationToken);
        await _inventoryClient.NotifyOrderAcceptedAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} accepted for customer {CustomerId}", order.OrderId, order.CustomerId);
        return order;
    }

    public Task<OrderRecord?> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        return _repository.GetAsync(orderId, cancellationToken);
    }

    private static void ValidateRequest(OrderCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            throw new ArgumentException("CustomerId is required.", nameof(request.CustomerId));
        }

        if (request.ItemSkus is null || request.ItemSkus.Count == 0)
        {
            throw new ArgumentException("At least one item SKU is required.", nameof(request.ItemSkus));
        }

        if (request.TotalAmount < 0)
        {
            throw new ArgumentException("TotalAmount must be non-negative.", nameof(request.TotalAmount));
        }
    }
}
