using System.Collections.Concurrent;
using OrderService.Models;

namespace OrderService.Services;

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<string, OrderRecord> _orders = new();

    public Task<OrderRecord> AddAsync(OrderRecord order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        return Task.FromResult(order);
    }

    public Task<OrderRecord?> GetAsync(string orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }
}
