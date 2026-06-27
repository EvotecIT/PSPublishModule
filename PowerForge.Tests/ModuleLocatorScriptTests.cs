using System;

namespace PowerForge.Tests;

public sealed class ModuleLocatorScriptTests
{
    [Fact]
    public void FindInstalledModule_RequiresScopeRootPathBoundary()
    {
        var script = PowerForgeScripts.Load("Scripts/ModuleLocator/Find-InstalledModule.ps1");

        Assert.Contains("$moduleBase.Equals($root", script, StringComparison.Ordinal);
        Assert.Contains("$rootWithSeparator", script, StringComparison.Ordinal);
        Assert.Contains("/usr/share/powershell/Modules", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$moduleBase.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInstalledVersions_SelectsLatestBySemanticVersionText()
    {
        var script = PowerForgeScripts.Load("Scripts/ModuleDependencyInstaller/Get-InstalledVersions.ps1");

        Assert.Contains("Compare-SemanticModuleVersion", script, StringComparison.Ordinal);
        Assert.Contains("Select-LatestModule", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Sort-Object Version -Descending", script, StringComparison.Ordinal);
    }
}
