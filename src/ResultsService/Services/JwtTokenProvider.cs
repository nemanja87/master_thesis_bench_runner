using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultsService.Options;
using Shared.Security;

namespace ResultsService.Services;

public class JwtTokenProvider
{
    private readonly BenchRunnerOptions _options;
    private readonly ILogger<JwtTokenProvider> _logger;

    public JwtTokenProvider(IOptions<BenchRunnerOptions> options, ILogger<JwtTokenProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> TryAcquireTokenAsync(bool requiresJwt, CancellationToken cancellationToken)
    {
        if (!requiresJwt)
        {
            return null;
        }

        var caCertificate = CertificateUtilities.TryLoadPemCertificate(_options.Security.Tls.CaCertificatePath, optional: true);
        X509Certificate2? clientCertificate = null;
        if (SecurityProfileDefaults.RequiresMtls())
        {
            clientCertificate = CertificateUtilities.TryLoadPemCertificate(
                _options.Security.Tls.ClientCertificatePath,
                _options.Security.Tls.ClientCertificateKeyPath,
                optional: true);
        }

        var handler = HttpHandlerFactory.CreateBackchannelHandler(caCertificate, clientCertificate);

        try
        {
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.Security.Jwt.ClientId,
                ["client_secret"] = _options.Security.Jwt.ClientSecret,
                ["scope"] = _options.Security.Jwt.Scope
            });

            using var response = await client.PostAsync(_options.Security.Jwt.TokenEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!json.RootElement.TryGetProperty("access_token", out var tokenElement))
            {
                throw new InvalidOperationException("Token response missing access_token.");
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Received empty access token.");
            }

            LogTokenPreview(token);
            return token;
        }
        finally
        {
            handler.Dispose();
        }
    }

    private void LogTokenPreview(string token)
    {
        try
        {
            var segments = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                _logger.LogInformation("Token resolved (len={Length}) payload_prefix=<unavailable>", token.Length);
                return;
            }

            var payloadSegment = segments[1];
            var padded = payloadSegment.PadRight(payloadSegment.Length + (4 - payloadSegment.Length % 4) % 4, '=');
            var payloadBytes = Convert.FromBase64String(padded);
            var payloadText = Encoding.UTF8.GetString(payloadBytes);
            var preview = payloadText.Length > 20 ? payloadText[..20] : payloadText;
            _logger.LogInformation("Token resolved (len={Length}) payload_prefix={Preview}", token.Length, preview);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log token preview safely.");
        }
    }
}
