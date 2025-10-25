using Shared.Security;

namespace Shared.Tests.Security;

public class SecurityProfileDefaultsTests
{
    [Theory]
    [InlineData(null, SecurityProfile.S0)]
    [InlineData("", SecurityProfile.S0)]
    [InlineData("S2", SecurityProfile.S2)]
    [InlineData("s3", SecurityProfile.S3)]
    public void Parse_ReturnsExpectedProfile(string? input, SecurityProfile expected)
    {
        Assert.Equal(expected, SecurityProfileDefaults.Parse(input));
    }

    [Fact]
    public void TryParse_ReturnsFalseForUnknownValue()
    {
        var success = SecurityProfileDefaults.TryParse("unknown", out var profile);

        Assert.False(success);
        Assert.Equal(SecurityProfile.S0, profile);
    }

    [Fact]
    public void RequiresFlagsAlignWithProfiles()
    {
        Assert.False(SecurityProfileDefaults.RequiresHttps(SecurityProfile.S0));
        Assert.True(SecurityProfileDefaults.RequiresHttps(SecurityProfile.S1));

        Assert.False(SecurityProfileDefaults.RequiresJwt(SecurityProfile.S1));
        Assert.True(SecurityProfileDefaults.RequiresJwt(SecurityProfile.S2));

        Assert.True(SecurityProfileDefaults.RequiresPerMethodPolicies(SecurityProfile.S4));
        Assert.False(SecurityProfileDefaults.RequiresPerMethodPolicies(SecurityProfile.S1));

        Assert.True(SecurityProfileDefaults.RequiresMtls(SecurityProfile.S4));
        Assert.False(SecurityProfileDefaults.RequiresMtls(SecurityProfile.S2));
    }

    [Fact]
    public void CurrentProfile_ReadsEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable(SecurityProfileDefaults.EnvironmentVariable);
        Environment.SetEnvironmentVariable(SecurityProfileDefaults.EnvironmentVariable, SecurityProfile.S3.ToString());

        try
        {
            Assert.Equal(SecurityProfile.S3, SecurityProfileDefaults.CurrentProfile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecurityProfileDefaults.EnvironmentVariable, original);
        }
    }
}
