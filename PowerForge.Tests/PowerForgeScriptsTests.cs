using Xunit;

namespace PowerForge.Tests;

public sealed class PowerForgeScriptsTests
{
    [Fact]
    public void Load_ImportModulesScript_IncludesDesktopCapacitySafeguardBeforeImports()
    {
        var script = PowerForgeScripts.Load("Scripts/ModulePipeline/Import-Modules.ps1");

        Assert.Contains("function Initialize-DesktopImportCapacity", script, StringComparison.Ordinal);
        Assert.Contains("$global:MaximumFunctionCount = $targetFunctionCount", script, StringComparison.Ordinal);
        Assert.Contains("$global:MaximumVariableCount = $targetVariableCount", script, StringComparison.Ordinal);

        var resetIndex = script.IndexOf("Reset-PSModulePathForEdition", StringComparison.Ordinal);
        var initializeCallIndex = script.LastIndexOf("Initialize-DesktopImportCapacity", StringComparison.Ordinal);
        var importRequiredIndex = script.IndexOf("if ($ImportRequired -eq '1')", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0);
        Assert.True(initializeCallIndex > resetIndex);
        Assert.True(importRequiredIndex > initializeCallIndex);
    }

    [Fact]
    public void Load_InvokeTestSuiteScript_UsesDedicatedBooleanForVerboseSwitch()
    {
        var script = PowerForgeScripts.Load("Scripts/Tests/Invoke-TestSuite.ps1");

        Assert.Matches(@"\[bool\]\s*\$ImportVerbose\b", script);
        Assert.Contains("function Import-TestSuiteModule", script, StringComparison.Ordinal);
        Assert.Contains("-Verbose:$ImportVerbose", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$importModulesVerboseEnabled", script, StringComparison.Ordinal);
    }
}
