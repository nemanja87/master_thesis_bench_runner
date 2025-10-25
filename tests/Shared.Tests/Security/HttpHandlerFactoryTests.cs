using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shared.Security;

namespace Shared.Tests.Security;

public class HttpHandlerFactoryTests
{
    [Fact]
    public void CreateBackchannelHandler_ConfiguresClientCertificates()
    {
        using var authority = CreateAuthority();
        using var client = CreateIssuedCertificate(authority);

        var handler = HttpHandlerFactory.CreateBackchannelHandler(authority, client);

        Assert.NotNull(handler.SslOptions.ClientCertificates);
        Assert.Single(handler.SslOptions.ClientCertificates);
        var configuredCert = Assert.IsAssignableFrom<X509Certificate2>(handler.SslOptions.ClientCertificates![0]);
        Assert.Equal(client.Thumbprint, configuredCert.Thumbprint);
        Assert.NotNull(handler.SslOptions.RemoteCertificateValidationCallback);

        using var serverCert = CreateIssuedCertificate(authority);
        var callback = handler.SslOptions.RemoteCertificateValidationCallback!;
        var valid = callback.Invoke(null!, serverCert, null!, SslPolicyErrors.RemoteCertificateNameMismatch);
        Assert.True(valid);
    }

    private static X509Certificate2 CreateAuthority()
    {
        var request = new CertificateRequest("CN=shared-tests-ca", RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static X509Certificate2 CreateIssuedCertificate(X509Certificate2 authority)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=shared-tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);
        var issuerLimit = authority.NotAfter.AddMinutes(-1);
        if (notAfter >= issuerLimit)
        {
            notAfter = issuerLimit;
        }

        var issued = request.Create(authority, notBefore, notAfter, serialNumber);
        return issued.CopyWithPrivateKey(key);
    }
}
