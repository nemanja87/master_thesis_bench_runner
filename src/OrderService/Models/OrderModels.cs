using System;
using System.Collections.Generic;

namespace OrderService.Models;

public record OrderCreateRequest(string CustomerId, IReadOnlyList<string> ItemSkus, decimal TotalAmount);

public record OrderAcceptedResponse(string OrderId, bool Accepted);

public record OrderDto(string OrderId, string CustomerId, IReadOnlyList<string> ItemSkus, decimal TotalAmount, DateTimeOffset CreatedAt);

public sealed class OrderRecord
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required IReadOnlyList<string> ItemSkus { get; init; }
    public required decimal TotalAmount { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
