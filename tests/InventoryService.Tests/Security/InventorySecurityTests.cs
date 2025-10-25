using System.IO;
using System.Net;
using System.Net.Http.Json;
using InventoryService.Models;
using InventoryService.Tests.Infrastructure;
using Shared.Security;

namespace InventoryService.Tests.Security;

public class InventorySecurityTests
{
    [Fact]
    public async Task ReserveInventory_ReturnsUnauthorized_WhenJwtRequiredAndMissingCredentials()
    {
        using var factory = new InventoryServiceTestFactory(SecurityProfile.S2, useTestAuthentication: true);
        var serverCertPath = Environment.GetEnvironmentVariable("BENCH_Security__Tls__ServerCertificatePath");
        Assert.False(string.IsNullOrEmpty(serverCertPath));
        Assert.True(File.Exists(serverCertPath));
        using var client = factory.CreateClient();

        var request = new InventoryReserveRequest("order-2", "cust-2", new[] { "sku" }, 10m);
        var response = await client.PostAsJsonAsync("/api/inventory/reserve", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReserveInventory_ReturnsForbidden_WhenScopeMissing()
    {
        using var factory = new InventoryServiceTestFactory(SecurityProfile.S2, useTestAuthentication: true);
        var serverCertPath = Environment.GetEnvironmentVariable("BENCH_Security__Tls__ServerCertificatePath");
        Assert.False(string.IsNullOrEmpty(serverCertPath));
        Assert.True(File.Exists(serverCertPath));
        using var client = factory.CreateClient();

        var request = new InventoryReserveRequest("order-5", "cust-5", new[] { "sku" }, 12m);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
        var response = await client.PostAsJsonAsync("/api/inventory/reserve", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReserveInventory_Succeeds_WhenScopeGranted()
    {
        using var factory = new InventoryServiceTestFactory(SecurityProfile.S2, useTestAuthentication: true);
        var serverCertPath = Environment.GetEnvironmentVariable("BENCH_Security__Tls__ServerCertificatePath");
        Assert.False(string.IsNullOrEmpty(serverCertPath));
        Assert.True(File.Exists(serverCertPath));
        using var client = factory.CreateClient();

        var request = new InventoryReserveRequest("order-6", "cust-6", new[] { "sku-x" }, 14m);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "on");
        client.DefaultRequestHeaders.Add("X-Test-Scopes", "inventory.write");

        var response = await client.PostAsJsonAsync("/api/inventory/reserve", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
