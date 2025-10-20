namespace ResultsService.Options;

public class BenchRunnerOptions
{
    public const string SectionName = "Bench";

    public TargetOptions Target { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public ToolOptions Tools { get; set; } = new();

    public class TargetOptions
    {
        public string RestBaseUrl { get; set; } = "http://gateway:8080";
        public string RestTlsBaseUrl { get; set; } = "https://gateway:8080";
        public string RestMtlsBaseUrl { get; set; } = "https://gateway:8080";
        public string GrpcAddress { get; set; } = "gateway:9090";
    }

    public class SecurityOptions
    {
        public JwtOptions Jwt { get; set; } = new();
        public TlsOptions Tls { get; set; } = new();
    }

    public class JwtOptions
    {
        public string TokenEndpoint { get; set; } = "https://authserver:5001/connect/token";
        public string ClientId { get; set; } = "bench-runner";
        public string ClientSecret { get; set; } = "bench-runner-secret";
        public string Scope { get; set; } = "orders.write orders.read inventory.write";
    }

    public class TlsOptions
    {
        public string CaCertificatePath { get; set; } = "/certs/ca/ca.crt.pem";
        public string ClientCertificatePath { get; set; } = string.Empty;
        public string ClientCertificateKeyPath { get; set; } = string.Empty;
    }

    public class ToolOptions
    {
        public string K6Path { get; set; } = "/usr/local/bin/k6";
        public string GhzPath { get; set; } = "/usr/local/bin/ghz";
        public string GhzProtoPath { get; set; } = "/app/protos/ordering.proto";
    }
}
