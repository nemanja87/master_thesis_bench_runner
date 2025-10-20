using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Shared.Security;

public static class HttpHandlerFactory
{
    public static SocketsHttpHandler CreateBackchannelHandler(
        X509Certificate2? caCertificate,
        X509Certificate2? clientCertificate = null)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions =
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            },
            EnableMultipleHttp2Connections = true
        };

        if (caCertificate is not null)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is not X509Certificate2 serverCertificate)
                {
                    return false;
                }

                if (errors == SslPolicyErrors.None)
                {
                    return true;
                }

                using var validationChain = new X509Chain
                {
                    ChainPolicy =
                    {
                        TrustMode = X509ChainTrustMode.CustomRootTrust,
                        RevocationMode = X509RevocationMode.NoCheck,
                        VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority
                    }
                };

                validationChain.ChainPolicy.CustomTrustStore.Add(caCertificate);
                return validationChain.Build(serverCertificate);
            };
        }

        if (clientCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
        }

        return handler;
    }
}
