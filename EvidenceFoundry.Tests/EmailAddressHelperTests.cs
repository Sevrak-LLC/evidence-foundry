using System.Collections.Generic;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class EmailAddressHelperTests
{
    [Fact]
    public void TryGenerateEmailReturnsFalseForInvalidDomain()
    {
        var success = EmailAddressHelper.TryGenerateEmail("Jane", "Doe", "invalid", out var email);

        Assert.False(success);
        Assert.Equal(string.Empty, email);
    }

    [Fact]
    public void TryGenerateEmailReturnsEmailForValidInputs()
    {
        var success = EmailAddressHelper.TryGenerateEmail("Jane", "Doe", "Example.COM", out var email);

        Assert.True(success);
        Assert.Equal("jane.doe@example.com", email);
    }

    [Fact]
    public void TryGenerateUniqueEmailAppendsCounterWhenUsed()
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
    public void GenerateUniqueEmailThrowsWhenInvalidDomain()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EmailAddressHelper.GenerateUniqueEmail("Jane", "Doe", "invalid", null));
    }
}
