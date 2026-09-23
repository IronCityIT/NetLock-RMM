using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NetLock_RMM_Web_Console.Configuration;
using Xunit;

namespace IronClad.WebConsole.Tests;

public class Branding_Tests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Product_name_defaults_to_iron_clad(string? configured)
    {
        Assert.Equal("Iron Clad Support", Branding.Product_Name(configured));
    }

    [Fact]
    public void Configured_product_name_wins()
    {
        Assert.Equal("Acme IT", Branding.Product_Name("  Acme IT "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    public void Source_url_falls_back_to_the_fork_for_unusable_values(string? configured)
    {
        Assert.Equal("https://github.com/IronCityIT/NetLock-RMM", Branding.Source_Code_Url(configured));
    }

    [Fact]
    public void Configured_http_source_url_is_used_without_trailing_slash()
    {
        Assert.Equal("https://git.example.com/ironclad", Branding.Source_Code_Url(" https://git.example.com/ironclad/ "));
    }

    [Fact]
    public void Default_source_offer_is_the_modified_fork_not_upstream()
    {
        // AGPL-3.0 section 13: offer the source of the modified version users interact with
        Assert.DoesNotContain("0x101-Cyber-Security", Branding.Default_Source_Code_Url);
        Assert.Equal("https://github.com/IronCityIT/NetLock-RMM/issues", Branding.Issues_Url(Branding.Default_Source_Code_Url));
    }

    [Fact]
    public void Two_factor_issuer_uses_product_name()
    {
        Assert.Equal("Iron Clad Support - Instance", Branding.Two_Factor_Issuer(Branding.Default_Product_Name));
    }
}

// Guards against upstream merges reintroducing hardcoded upstream branding on the rebranded surfaces.
public class Console_Source_Scan_Tests
{
    private static string Console_Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "NetLock-RMM-Web-Console"));

    [Theory]
    [InlineData("Components/App.razor")]
    [InlineData("Components/Layout/MainLayout.razor")]
    [InlineData("Components/Pages/Account/Login.razor")]
    public void Rebranded_surfaces_have_no_hardcoded_upstream_name_or_links(string relative)
    {
        string text = File.ReadAllText(Path.Combine(Console_Root(), relative));

        Assert.DoesNotMatch(new Regex("<title>[^<]*NetLock RMM"), text);
        Assert.DoesNotMatch(new Regex("(?i)alt=\"NetLock RMM"), text);
        Assert.DoesNotContain("github.com/0x101-Cyber-Security/NetLock-RMM", text);
        Assert.DoesNotContain("GenerateSetupCode(\"NetLock", text);
    }
}
