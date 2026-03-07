using System;
using System.Reflection;

namespace PowerForge.Tests;

public sealed class ModulePipelineImportValidationTests
{
    [Fact]
    public void GetImportValidationTargets_PrefersDesktopThenCore_OnWindows()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return;

        var method = typeof(ModulePipelineRunner).GetMethod(
            "GetImportValidationTargets",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var targets = (Array?)method!.Invoke(null, new object?[] { new[] { "Desktop", "Core" } });
        Assert.NotNull(targets);
        Assert.Equal(2, targets!.Length);

        var first = targets.GetValue(0)!;
        var second = targets.GetValue(1)!;

        Assert.Equal(
            "Windows PowerShell/Desktop",
            first.GetType().GetProperty("Label")!.GetValue(first));
        Assert.Equal(
            false,
            first.GetType().GetProperty("PreferPwsh")!.GetValue(first));

        Assert.Equal(
            "PowerShell/Core",
            second.GetType().GetProperty("Label")!.GetValue(second));
        Assert.Equal(
            true,
            second.GetType().GetProperty("PreferPwsh")!.GetValue(second));
    }
}
