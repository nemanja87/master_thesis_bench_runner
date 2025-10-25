using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Shared.Security;

namespace InventoryService.Tests.Infrastructure;

internal sealed class InventoryServiceTestFactory : WebApplicationFactory<Program>
{
    private readonly bool _useTestAuthentication;
    private readonly TestCertificateMaterial _certificates;

    public InventoryServiceTestFactory(SecurityProfile profile, bool useTestAuthentication = false)
    {
        _useTestAuthentication = useTestAuthentication;
        _certificates = TestCertificateMaterial.Create(profile);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");

        if (_useTestAuthentication)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["BENCH_Security__Tls__ServerCertificatePath"] = _certificates.ServerCertificatePath,
                ["BENCH_Security__Tls__CaCertificatePath"] = _certificates.CaCertificatePath,
                ["BENCH_Security__Tls__ClientCertificatePath"] = _certificates.ClientCertificatePath,
                ["BENCH_Security__Tls__ClientCertificateKeyPath"] = _certificates.ClientKeyPath
            };

            config.Sources.Insert(0, new MemoryConfigurationSource { InitialData = overrides });
        });

        builder.ConfigureServices(services =>
        {
            services.AddHttpContextAccessor();
        });
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _certificates.Dispose();
        }

        base.Dispose(disposing);
    }
}
