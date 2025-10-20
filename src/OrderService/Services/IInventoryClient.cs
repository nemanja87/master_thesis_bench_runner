using OrderService.Models;

namespace OrderService.Services;

public interface IInventoryClient
{
    Task NotifyOrderAcceptedAsync(OrderRecord order, CancellationToken cancellationToken = default);
}
