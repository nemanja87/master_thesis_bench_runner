using System.Linq;
using Grpc.Core;
using Shared.Contracts.Orders;
using Shared.Security;

namespace OrderService.Services;

public class OrderGrpcService : Shared.Contracts.Orders.OrderService.OrderServiceBase
{
    private readonly OrderApplicationService _orderApplicationService;

    public OrderGrpcService(OrderApplicationService orderApplicationService)
    {
        _orderApplicationService = orderApplicationService;
    }

    public override async Task<OrderCreateResponse> Create(OrderCreateRequest request, ServerCallContext context)
    {
        EnsureAuthorized(context);

        var orderRecord = await _orderApplicationService.CreateOrderAsync(new Models.OrderCreateRequest(
            request.CustomerId,
            request.ItemSkus.ToList(),
            (decimal)request.TotalAmount
        ), context.CancellationToken);

        return new OrderCreateResponse
        {
            OrderId = orderRecord.OrderId,
            Accepted = true
        };
    }

    private static void EnsureAuthorized(ServerCallContext context)
    {
        if (!SecurityProfileDefaults.RequiresJwt())
        {
            return;
        }

        var user = context.GetHttpContext().User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Authentication required."));
        }

        if (SecurityProfileDefaults.RequiresPerMethodPolicies() && !user.HasScope("orders.write"))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "orders.write scope required."));
        }
    }
}
