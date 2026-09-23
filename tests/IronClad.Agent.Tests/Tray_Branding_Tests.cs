using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NetLock_RMM_Tray_Icon;
using Xunit;

namespace IronClad.Agent.Tests;

public class Tray_Branding_Tests
{
    private static string Repo_Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void Client_facing_titles_use_the_iron_clad_name()
    {
        foreach (string title in new[] { Branding.Product_Name, Branding.Actions_Window_Title, Branding.Chat_Window_Title, Branding.About_Window_Title })
        {
            Assert.Contains("Iron Clad Support", title);
            Assert.DoesNotContain("NetLock", title);
        }
    }

    [Fact]
    public void Legal_notice_keeps_upstream_attribution_and_license()
    {
        // AGPL-3.0 section 5(d): interactive interfaces must keep Appropriate Legal Notices
        string notice = Branding.Legal_Notice(2026);

        Assert.Contains("Iron Clad Support", notice);
        Assert.Contains("NetLock RMM", notice);
        Assert.Contains("0x101 GmbH", notice);
        Assert.Contains("2026", notice);
        Assert.Contains("AGPL", notice);
    }

    // Guards against upstream merges reintroducing hardcoded product names in end-user UI.
    [Fact]
    public void Tray_ui_has_no_hardcoded_netlock_titles_or_labels()
    {
        string tray = Path.Combine(Repo_Root(), "NetLock RMM Tray Icon");
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(tray, "*.axaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "(Title|Text|Content|ToolTip\\.Tip)=\"[^\"]*NetLock[^\"]*\""))
                offenders.Add(Path.GetFileName(file) + ": " + m.Value);

        foreach (string file in Directory.EnumerateFiles(tray, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\\?\\?\\s*\\$?\"[^\"]*NetLock[^\"]*\""))
                offenders.Add(Path.GetFileName(file) + ": " + m.Value);

        Assert.Empty(offenders);
    }
}
