using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shared.Security;

namespace Shared.Tests.Security;

public class CertificateUtilitiesTests
{
    [Fact]
    public void TryLoadPfxCertificate_ReturnsNull_WhenOptionalAndMissing()
    {
        var result = CertificateUtilities.TryLoadPfxCertificate("/path/does/not/exist.pfx", optional: true);
        Assert.Null(result);
    }

    [Fact]
    public void TryLoadPfxCertificate_Throws_WhenRequiredAndMissing()
    {
        Assert.Throws<FileNotFoundException>(() => CertificateUtilities.TryLoadPfxCertificate("/path/does/not/exist.pfx", optional: false));
    }

    [Fact]
    public void LoadAndNormalizeCertificates_FromPemAndPfx()
    {
        using var fixture = TestCertificateFixture.Create();

        var pfx = CertificateUtilities.TryLoadPfxCertificate(fixture.PfxPath, optional: false);
        Assert.NotNull(pfx);
        Assert.Equal(fixture.Certificate.Thumbprint, pfx!.Thumbprint);

        var pem = CertificateUtilities.TryLoadPemCertificate(fixture.CertPemPath, fixture.KeyPemPath, optional: false);
        Assert.NotNull(pem);
        Assert.Equal(fixture.Certificate.Thumbprint, pem!.Thumbprint);

        var normalized = CertificateUtilities.NormalizeCertificate(pem);
        Assert.Equal(pem.Thumbprint, normalized.Thumbprint);
    }
}

internal sealed class TestCertificateFixture : IDisposable
{
    private TestCertificateFixture(X509Certificate2 certificate, string pfxPath, string certPemPath, string keyPemPath)
    {
        Certificate = certificate;
        PfxPath = pfxPath;
        CertPemPath = certPemPath;
        KeyPemPath = keyPemPath;
    }

    public X509Certificate2 Certificate { get; }
    public string PfxPath { get; }
    public string CertPemPath { get; }
    public string KeyPemPath { get; }

    public static TestCertificateFixture Create()
    {
        var request = new CertificateRequest("CN=shared-tests", RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var pfxBytes = cert.Export(X509ContentType.Pkcs12);
        var pfxPath = WriteTempFile(pfxBytes, ".pfx");

        var certPem = PemEncoding.Write("CERTIFICATE", cert.RawData);
        var certPemPath = WriteTextFile(certPem, ".pem");

        using var rsa = cert.GetRSAPrivateKey()!;
        var keyPem = PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey());
        var keyPemPath = WriteTextFile(keyPem, ".key.pem");

        return new TestCertificateFixture(cert, pfxPath, certPemPath, keyPemPath);
    }

    private static string WriteTempFile(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string WriteTextFile(ReadOnlySpan<char> contents, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllText(path, contents.ToString());
        return path;
    }

    public void Dispose()
    {
        Certificate.Dispose();
        TryDelete(PfxPath);
        TryDelete(CertPemPath);
        TryDelete(KeyPemPath);
    }

    private static void TryDelete(string path)
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
}
