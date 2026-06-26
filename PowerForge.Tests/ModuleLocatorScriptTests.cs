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
        Assert.DoesNotContain("$moduleBase.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)", script, StringComparison.Ordinal);
    }
}
