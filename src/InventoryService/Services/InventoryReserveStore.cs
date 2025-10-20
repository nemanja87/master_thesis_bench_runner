using System.Collections.Concurrent;
using System.Collections.Generic;
using InventoryService.Models;

namespace InventoryService.Services;

public class InventoryReserveStore
{
    private readonly ConcurrentQueue<InventoryReserveRecord> _recent = new();
    private readonly int _maxEntries;

    public InventoryReserveStore(int maxEntries = 10)
    {
        _maxEntries = maxEntries;
    }

    public void Record(InventoryReserveRecord record)
    {
        _recent.Enqueue(record);
        while (_recent.Count > _maxEntries && _recent.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyCollection<InventoryReserveRecord> Snapshot() => _recent.ToArray();
}
