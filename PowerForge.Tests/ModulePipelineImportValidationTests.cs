using System;

namespace PowerForge.Tests;

public sealed class ModulePipelineImportValidationTests
{
    [Fact]
    public void GetImportValidationTargets_PrefersDesktopThenCore_OnWindows()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return;

        var targets = ModulePipelineRunner.GetImportValidationTargets(new[] { "Desktop", "Core" });
        Assert.Equal(2, targets.Length);

        var first = targets[0];
        var second = targets[1];

        Assert.Equal(
            "Windows PowerShell/Desktop",
            first.Label);
        Assert.False(first.PreferPwsh);

        Assert.Equal(
            "PowerShell/Core",
            second.Label);
        Assert.True(second.PreferPwsh);
    }
}
