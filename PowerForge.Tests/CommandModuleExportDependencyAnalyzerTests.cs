using System.IO;

namespace PowerForge.Tests;

public class CommandModuleExportDependencyAnalyzerTests
{
    [Fact]
    public void Analyze_InferModuleOnlyDependency_FromKnownCommandPattern()
    {
        var root = CreateTempRoot();
        try
        {
            var publicDir = Directory.CreateDirectory(Path.Combine(root, "Public"));
            var needsAd = Path.Combine(publicDir.FullName, "Get-NeedsAD.ps1");
            var always = Path.Combine(publicDir.FullName, "Get-Always.ps1");
            File.WriteAllText(needsAd, "function Get-NeedsAD { Get-ADUser -Filter * }");
            File.WriteAllText(always, "function Get-Always { Get-Date }");

            var dependencies = CommandModuleExportDependencyAnalyzer.Analyze(
                new[] { needsAd, always },
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ActiveDirectory"] = Array.Empty<string>()
                },
                new[] { "Get-NeedsAD", "Get-Always" });

            var activeDirectory = Assert.Single(dependencies);
            Assert.Equal("ActiveDirectory", activeDirectory.Key);
            Assert.Equal(new[] { "Get-NeedsAD" }, activeDirectory.Value);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Analyze_PropagatesModuleDependency_ThroughPrivateHelper()
    {
        var root = CreateTempRoot();
        try
        {
            var publicDir = Directory.CreateDirectory(Path.Combine(root, "Public"));
            var privateDir = Directory.CreateDirectory(Path.Combine(root, "Private"));
            var publicFunction = Path.Combine(publicDir.FullName, "Get-NeedsAD.ps1");
            var privateFunction = Path.Combine(privateDir.FullName, "Invoke-ADLookup.ps1");
            File.WriteAllText(publicFunction, "function Get-NeedsAD { Invoke-ADLookup }");
            File.WriteAllText(privateFunction, "function Invoke-ADLookup { Get-ADObject -Filter * }");

            var dependencies = CommandModuleExportDependencyAnalyzer.Analyze(
                new[] { publicFunction, privateFunction },
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ActiveDirectory"] = Array.Empty<string>()
                },
                new[] { "Get-NeedsAD" });

            Assert.True(dependencies.TryGetValue("ActiveDirectory", out var activeDirectory));
            Assert.Equal(new[] { "Get-NeedsAD" }, activeDirectory);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Analyze_TreatsExplicitExportedCommandNames_AsConditionalExports()
    {
        var dependencies = CommandModuleExportDependencyAnalyzer.Analyze(
            Array.Empty<string>(),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Contoso.Optional"] = new[] { "Get-OptionalCommand", "Invoke-ExternalThing" }
            },
            new[] { "Get-OptionalCommand", "Get-Always" });

        Assert.True(dependencies.TryGetValue("Contoso.Optional", out var contoso));
        Assert.Equal(new[] { "Get-OptionalCommand" }, contoso);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
