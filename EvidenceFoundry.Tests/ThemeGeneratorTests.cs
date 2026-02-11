using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class ThemeGeneratorTests
{
    [Fact]
    public void ApplyThemeResponse_FiltersUnknownDomainsAndNormalizes()
    {
        var domainOrgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["acme.com"] = "Acme Corp",
            ["globex.com"] = "Globex"
        };

        var response = new ThemeGenerator.ThemeApiResponse
        {
            Themes =
            [
                new ThemeGenerator.ThemeDto
                {
                    Domain = "HTTP://ACME.COM/",
                    OrganizationName = "Wrong Name",
                    ThemeName = "Custom",
                    PrimaryColor = "112233",
                    SecondaryColor = "445566",
                    AccentColor = "778899",
                    HeadingFont = "Segoe UI",
                    BodyFont = "Calibri"
                },
                new ThemeGenerator.ThemeDto
                {
                    Domain = "unknown.com",
                    OrganizationName = "Unknown"
                }
            ]
        };

        var themes = new Dictionary<string, OrganizationTheme>(StringComparer.OrdinalIgnoreCase);

        ThemeGenerator.ApplyThemeResponse(themes, domainOrgs, response);

        var theme = Assert.Single(themes).Value;
        Assert.Equal("acme.com", theme.Domain);
        Assert.Equal("Acme Corp", theme.OrganizationName);
        Assert.Equal("Custom", theme.ThemeName);
    }
}
