using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Shared.Security;

namespace InventoryService.Tests.Infrastructure;

internal sealed class TestCertificateMaterial : IDisposable
{
    private static readonly string[] TrackedEnvironmentKeys =
    [
        SecurityProfileDefaults.EnvironmentVariable,
        "BENCH_Security__Tls__ServerCertificatePath",
        "BENCH_Security__Tls__CaCertificatePath",
        "BENCH_Security__Tls__ClientCertificatePath",
        "BENCH_Security__Tls__ClientCertificateKeyPath"
    ];

    private readonly List<string> _paths = [];
    private readonly Dictionary<string, string?> _originalEnvironmentValues;

    private TestCertificateMaterial(
        string serverPfxPath,
        string caCertificatePath,
        string clientCertificatePath,
        string clientKeyPath,
        Dictionary<string, string?> originalEnvironmentValues)
    {
        ServerCertificatePath = serverPfxPath;
        CaCertificatePath = caCertificatePath;
        ClientCertificatePath = clientCertificatePath;
        ClientKeyPath = clientKeyPath;
        _originalEnvironmentValues = originalEnvironmentValues;
    }

    public string ServerCertificatePath { get; }
    public string CaCertificatePath { get; }
    public string ClientCertificatePath { get; }
    public string ClientKeyPath { get; }

    public static TestCertificateMaterial Create(SecurityProfile profile)
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);

        using var authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest("CN=BenchRunner-Test-CA", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(authorityRequest.PublicKey, false));
        using var authorityCertificate = authorityRequest.CreateSelfSigned(notBefore, notAfter);

        using var serverCert = CreateIssuedCertificate("CN=inventoryservice-tests", authorityCertificate, authorityKey, notBefore, notAfter, out var serverKey);
        using var _serverKey = serverKey;
        using var clientCert = CreateIssuedCertificate("CN=inventoryservice-client", authorityCertificate, authorityKey, notBefore, notAfter, out var clientKey);
        using var _clientKey = clientKey;

        var serverPfxPath = WriteTempFile(serverCert.Export(X509ContentType.Pkcs12), ".pfx");
        var caPemPath = WritePemFile(authorityCertificate.RawData, "CERTIFICATE");
        var clientCertPemPath = WritePemFile(clientCert.RawData, "CERTIFICATE");
        var clientKeyPemPath = WritePemFile(clientKey.ExportPkcs8PrivateKey(), "PRIVATE KEY");

        var originalValues = CaptureCurrentEnvironment();

        var material = new TestCertificateMaterial(
            serverPfxPath,
            caPemPath,
            clientCertPemPath,
            clientKeyPemPath,
            originalValues);

        material._paths.AddRange([serverPfxPath, caPemPath, clientCertPemPath, clientKeyPemPath]);
        material.ApplyEnvironmentOverrides(profile);
        return material;
    }

    private static Dictionary<string, string?> CaptureCurrentEnvironment()
    {
        var snapshot = new Dictionary<string, string?>();
        foreach (var key in TrackedEnvironmentKeys)
        {
            snapshot[key] = Environment.GetEnvironmentVariable(key);
        }

        return snapshot;
    }

    private void ApplyEnvironmentOverrides(SecurityProfile profile)
    {
        Environment.SetEnvironmentVariable(SecurityProfileDefaults.EnvironmentVariable, profile.ToString());
        Environment.SetEnvironmentVariable("BENCH_Security__Tls__ServerCertificatePath", ServerCertificatePath);
        Environment.SetEnvironmentVariable("BENCH_Security__Tls__CaCertificatePath", CaCertificatePath);
        Environment.SetEnvironmentVariable("BENCH_Security__Tls__ClientCertificatePath", ClientCertificatePath);
        Environment.SetEnvironmentVariable("BENCH_Security__Tls__ClientCertificateKeyPath", ClientKeyPath);
    }

    private static X509Certificate2 CreateIssuedCertificate(
        string subject,
        X509Certificate2 issuerCertificate,
        RSA issuerKey,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        out RSA key)
    {
        key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var issued = request.Create(issuerCertificate, notBefore, notAfter, serialNumber);
        return issued.CopyWithPrivateKey(key);
    }

    private static string WriteTempFile(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string WritePemFile(byte[] bytes, string label)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"-----BEGIN {label}-----");
        builder.AppendLine(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine($"-----END {label}-----");
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        foreach (var (key, value) in _originalEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
