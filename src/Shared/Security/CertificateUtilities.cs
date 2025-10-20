using System.IO;
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Shared.Security;

public static class CertificateUtilities
{
    public static X509Certificate2? TryLoadPfxCertificate(string? path, string? password = null, bool optional = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return optional ? null : throw new ArgumentException("Certificate path must be provided.");
        }

        if (!File.Exists(path))
        {
            if (optional)
            {
                return null;
            }

            throw new FileNotFoundException($"Certificate not found at '{path}'.", path);
        }

        return LoadPfxCertificate(path!, password);
    }

    public static X509Certificate2 LoadPfxCertificate(string path, string? password = null)
    {
        var flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet;
        return string.IsNullOrEmpty(password)
            ? X509CertificateLoader.LoadPkcs12FromFile(path, ReadOnlySpan<char>.Empty, flags, default)
            : X509CertificateLoader.LoadPkcs12FromFile(path, password, flags, default);
    }

    public static X509Certificate2? TryLoadPemCertificate(string? certificatePath, string? privateKeyPath = null, bool optional = true)
    {
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return optional ? null : throw new ArgumentException("Certificate path must be provided.");
        }

        if (!File.Exists(certificatePath))
        {
            if (optional)
            {
                return null;
            }

            throw new FileNotFoundException($"Certificate not found at '{certificatePath}'.", certificatePath);
        }

        try
        {
            X509Certificate2 cert;
            if (privateKeyPath is { Length: > 0 } && File.Exists(privateKeyPath))
            {
                cert = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
            }
            else
            {
                var pemContent = File.ReadAllText(certificatePath);
                cert = X509Certificate2.CreateFromPem(pemContent);
            }

            return NormalizeCertificate(cert);
        }
        catch (CryptographicException) when (optional)
        {
            return null;
        }
    }

    public static X509Certificate2 NormalizeCertificate(X509Certificate2 certificate)
    {
        var flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet;
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), ReadOnlySpan<char>.Empty, flags, default);
    }
}
