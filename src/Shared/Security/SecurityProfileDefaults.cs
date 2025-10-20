using System.Diagnostics.CodeAnalysis;

namespace Shared.Security;

public static class SecurityProfileDefaults
{
    public const string EnvironmentVariable = "SEC_PROFILE";
    public const string DefaultProfile = "S0";

    private static readonly HashSet<SecurityProfile> JwtProfiles =
    [
        SecurityProfile.S2,
        SecurityProfile.S4
    ];

    private static readonly HashSet<SecurityProfile> MtlsProfiles =
    [
        SecurityProfile.S3,
        SecurityProfile.S4
    ];

    public static SecurityProfile CurrentProfile => Parse(
        Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static bool RequiresHttps() => RequiresHttps(CurrentProfile);

    public static bool RequiresHttps(SecurityProfile profile) =>
        profile switch
        {
            SecurityProfile.S0 => false,
            _ => true
        };

    public static bool RequiresMtls() => RequiresMtls(CurrentProfile);

    public static bool RequiresMtls(SecurityProfile profile) =>
        MtlsProfiles.Contains(profile);

    public static bool RequiresJwt() => RequiresJwt(CurrentProfile);

    public static bool RequiresJwt(SecurityProfile profile) =>
        JwtProfiles.Contains(profile);

    public static bool RequiresPerMethodPolicies() => RequiresPerMethodPolicies(CurrentProfile);

    public static bool RequiresPerMethodPolicies(SecurityProfile profile) =>
        JwtProfiles.Contains(profile);

    public static SecurityProfile Parse(string? value)
    {
        if (TryParse(value, out var profile))
        {
            return profile;
        }

        return SecurityProfile.S0;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out SecurityProfile profile)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            profile = SecurityProfile.S0;
            return true;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out profile))
        {
            return true;
        }

        profile = SecurityProfile.S0;
        return false;
    }
}

public enum SecurityProfile
{
    S0,
    S1,
    S2,
    S3,
    S4
}
