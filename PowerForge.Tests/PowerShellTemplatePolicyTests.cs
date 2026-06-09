using System.Text.RegularExpressions;

namespace PowerForge.Tests;

public class PowerShellTemplatePolicyTests
{
    [Fact]
    public void Services_ShouldNotContainInlinePowerShellHereStrings()
    {
        var repoRoot = FindRepoRoot();
        var servicesRoot = Path.Combine(repoRoot, "PowerForge", "Services");
        Assert.True(Directory.Exists(servicesRoot), $"Services folder not found: {servicesRoot}");

        var candidates = Directory.EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scriptHereStringPattern = new Regex(
            "@\"\\s*(#requires\\s+-Version|\\[CmdletBinding\\s*\\(\\s*\\)\\]|param\\s*\\()",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var offenders = new List<string>();
        foreach (var file in candidates)
        {
            var text = File.ReadAllText(file);
            if (!scriptHereStringPattern.IsMatch(text))
                continue;

            offenders.Add(Path.GetRelativePath(repoRoot, file));
        }

        Assert.True(
            offenders.Count == 0,
            "Inline PowerShell here-strings detected. Use embedded script templates under PowerForge/Scripts instead. Offenders: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void ModuleBootstrapper_Templates_ShouldExist()
    {
        var repoRoot = FindRepoRoot();
        var templateRoot = Path.Combine(repoRoot, "PowerForge", "Scripts", "ModuleBootstrapper");

        Assert.True(File.Exists(Path.Combine(templateRoot, "Bootstrapper.Template.ps1")));
        Assert.True(File.Exists(Path.Combine(templateRoot, "BinaryLoader.Template.ps1")));
        Assert.True(File.Exists(Path.Combine(templateRoot, "ScriptLoader.Template.ps1")));
        Assert.True(File.Exists(Path.Combine(templateRoot, "Libraries.Template.ps1")));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            var marker = Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj");
            if (File.Exists(marker))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for PowerShell template policy tests.");
    }
}
