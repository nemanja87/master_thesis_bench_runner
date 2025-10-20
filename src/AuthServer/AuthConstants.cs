namespace AuthServer;

public static class AuthConstants
{
    public const string Issuer = "https://authserver:5001";
    public const string ClientId = "bench-runner";
    public const string ClientSecret = "bench-runner-secret";

    public static readonly string[] AllowedScopes =
    [
        "orders.read",
        "orders.write",
        "inventory.write"
    ];
}
