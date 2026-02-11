using System.Collections.Generic;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class EmailAddressHelperTests
{
    [Fact]
    public void TryGenerateEmail_ReturnsFalse_ForInvalidDomain()
    {
        var success = EmailAddressHelper.TryGenerateEmail("Jane", "Doe", "invalid", out var email);

        Assert.False(success);
        Assert.Equal(string.Empty, email);
    }

    [Fact]
    public void TryGenerateEmail_ReturnsEmail_ForValidInputs()
    {
        var success = EmailAddressHelper.TryGenerateEmail("Jane", "Doe", "Example.COM", out var email);

        Assert.True(success);
        Assert.Equal("jane.doe@example.com", email);
    }

    [Fact]
    public void TryGenerateUniqueEmail_AppendsCounter_WhenUsed()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "john.smith@example.com"
        };

        var success = EmailAddressHelper.TryGenerateUniqueEmail(
            "John",
            "Smith",
            "example.com",
            used,
            out var email);

        Assert.True(success);
        Assert.Equal("john.smith2@example.com", email);
    }

    [Fact]
    public void GenerateUniqueEmail_Throws_WhenInvalidDomain()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EmailAddressHelper.GenerateUniqueEmail("Jane", "Doe", "invalid", null));
    }
}
