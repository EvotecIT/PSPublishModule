using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineRunnerImportTargetsTests
{
    [Fact]
    public void GetImportValidationTargets_SkipsDesktop_WhenStagingHasNoDefaultPayload()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var stagingPath = CreateStagingPath();
        try
        {
            CreateBinaryPayload(stagingPath, "Core");

            var targets = ModulePipelineRunner.GetImportValidationTargets(new[] { "Desktop", "Core" }, stagingPath);

            var target = Assert.Single(targets);
            Assert.Equal("Core", target.PowerShellEdition);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }
    }

    [Fact]
    public void GetImportValidationTargets_UsesAvailableBinaryPayloads_WhenEditionsAreUnspecified()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var stagingPath = CreateStagingPath();
        try
        {
            CreateBinaryPayload(stagingPath, "Default");

            var targets = ModulePipelineRunner.GetImportValidationTargets(Array.Empty<string>(), stagingPath);

            var target = Assert.Single(targets);
            Assert.Equal("Desktop", target.PowerShellEdition);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }
    }

    [Fact]
    public void GetImportValidationTargets_UsesCoreOnly_WhenCompatiblePSEditionsAreCoreOnly()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var targets = ModulePipelineRunner.GetImportValidationTargets(new[] { "Core" });

        var target = Assert.Single(targets);
        Assert.Equal("Core", target.PowerShellEdition);
        Assert.True(target.PreferPwsh);
    }

    [Fact]
    public void GetImportValidationTargets_SkipsDesktop_WhenMinimumPowerShellVersionRequiresPwsh()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var stagingPath = CreateStagingPath();
        try
        {
            CreateBinaryPayload(stagingPath, "Default");

            var targets = ModulePipelineRunner.GetImportValidationTargets(
                new[] { "Desktop", "Core" },
                stagingPath,
                minimumPowerShellVersion: "7.5");

            var target = Assert.Single(targets);
            Assert.Equal("Core", target.PowerShellEdition);
            Assert.True(target.PreferPwsh);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }
    }

    private static string CreateStagingPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateBinaryPayload(string stagingPath, string folderName)
    {
        var payloadPath = Path.Combine(stagingPath, "Lib", folderName);
        Directory.CreateDirectory(payloadPath);
        File.WriteAllText(Path.Combine(payloadPath, "PSPublishModule.dll"), string.Empty);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup for test temp directories
        }
    }
}
