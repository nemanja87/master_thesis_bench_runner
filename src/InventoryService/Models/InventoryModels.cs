using System;
using System.Collections.Generic;

namespace InventoryService.Models;

public record InventoryReserveRequest(string OrderId, string CustomerId, IReadOnlyList<string> ItemSkus, decimal TotalAmount);

public record InventoryReserveRecord(string OrderId, string CustomerId, DateTimeOffset RequestedAt, IReadOnlyList<string> ItemSkus, decimal TotalAmount);
