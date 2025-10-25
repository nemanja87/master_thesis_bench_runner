using InventoryService.Models;
using InventoryService.Services;

namespace InventoryService.Tests.Unit;

public class InventoryReserveStoreTests
{
    [Fact]
    public void Record_StoresEntriesUpToLimit()
    {
        var store = new InventoryReserveStore(maxEntries: 3);

        for (var i = 0; i < 5; i++)
        {
            var record = new InventoryReserveRecord($"order-{i}", "cust", DateTimeOffset.UtcNow, new[] { "sku" }, 10m);
            store.Record(record);
        }

        var snapshot = store.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.DoesNotContain(snapshot, r => r.OrderId == "order-0");
        Assert.Contains(snapshot, r => r.OrderId == "order-4");
    }

    [Fact]
    public void Snapshot_ReturnsCopyOfRecords()
    {
        var store = new InventoryReserveStore(maxEntries: 1);
        var record = new InventoryReserveRecord("order-1", "cust-1", DateTimeOffset.UtcNow, new[] { "sku" }, 10m);
        store.Record(record);

        var snapshot = store.Snapshot();
        Assert.Single(snapshot);
        Assert.Contains(record, snapshot);
    }
}
