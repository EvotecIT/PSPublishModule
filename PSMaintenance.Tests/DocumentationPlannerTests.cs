using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PSMaintenance.Tests;

public class DocumentationPlannerTests
{
    private string CreateTempModule(out string internals)
    {
        var root = Path.Combine(Path.GetTempPath(), "PGTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Readme\nHello");
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), "# Changelog\n- v1");
        File.WriteAllText(Path.Combine(root, "LICENSE"), "License");
        internals = Path.Combine(root, "Internals");
        Directory.CreateDirectory(internals);
        return root;
    }

    [Fact]
    public void DefaultSelection_Includes_Readme_Changelog_License()
    {
        var root = CreateTempModule(out var internals);
        var finder = new DocumentationFinder(new DummyCmdlet());
        var planner = new DocumentationPlanner(finder);

        var req = new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            PreferInternals = false,
            TitleName = "TestMod",
            TitleVersion = "1.0.0"
        };
        var res = planner.Execute(req);
        Assert.True(res.Items.Count >= 3);
        Assert.Contains(res.Items, i => i.Title.Contains("Readme", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.Items, i => i.Title.Contains("Changelog", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.Items, i => i.Title.Contains("License", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DummyCmdlet : System.Management.Automation.PSCmdlet { }
}

