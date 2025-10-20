using OrderService.Models;

namespace OrderService.Services;

public interface IOrderRepository
{
    Task<OrderRecord> AddAsync(OrderRecord order, CancellationToken cancellationToken = default);

    Task<OrderRecord?> GetAsync(string orderId, CancellationToken cancellationToken = default);
}
