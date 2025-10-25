using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryService.Models;
using InventoryService.Services;
using InventoryService.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shared.Security;

namespace InventoryService.Tests.Integration;

public class InventoryEndpointsTests
{
    [Fact]
    public async Task ReserveInventory_ReturnsNoContent_AndPersistsRecord_InS0Profile()
    {
        using var factory = new InventoryServiceTestFactory(SecurityProfile.S0);
        using var client = factory.CreateClient();

        var request = new InventoryReserveRequest("order-1", "cust-1", new[] { "sku-1", "sku-2" }, 25.5m);
        var response = await client.PostAsJsonAsync("/api/inventory/reserve", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var store = factory.Services.GetRequiredService<InventoryReserveStore>();
        var snapshot = store.Snapshot();
        var record = Assert.Single(snapshot);
        Assert.Equal(request.OrderId, record.OrderId);
        Assert.Equal(request.CustomerId, record.CustomerId);
        Assert.Equal(request.TotalAmount, record.TotalAmount);
        Assert.Equal(request.ItemSkus.Count, record.ItemSkus.Count);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsProfileAndRecentReservations()
    {
        using var factory = new InventoryServiceTestFactory(SecurityProfile.S0);
        using var client = factory.CreateClient();

        var request = new InventoryReserveRequest("order-3", "cust-77", new[] { "sku-10" }, 42m);
        await client.PostAsJsonAsync("/api/inventory/reserve", request);

        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(SecurityProfile.S0.ToString(), payload.RootElement.GetProperty("profile").GetString());
        var recentReservations = payload.RootElement.GetProperty("recentReservations");
        Assert.Equal(1, recentReservations.GetArrayLength());
        Assert.Equal(request.OrderId, recentReservations[0].GetProperty("orderId").GetString());
    }
}
