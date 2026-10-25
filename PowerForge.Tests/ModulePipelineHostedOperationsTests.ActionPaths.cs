using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineHostedOperationsTests
{
    [Theory]
    [InlineData(@".\Build", @".\Build\Verify-Path.ps1")]
    [InlineData("./Build", "./Build/Verify-Path.ps1")]
    [InlineData("\".\\Build\"", "\".\\Build\\Verify-Path.ps1\"")]
    [InlineData(null, null)]
    public void RunAction_ExecutesFileWithPortablePaths(string? workingDirectory, string? filePath)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"), "Project with spaces"));
        try
        {
            var buildPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Build")).FullName;
            File.WriteAllText(Path.Combine(buildPath, "input.txt"), "action input");
            var scriptPath = Path.Combine(buildPath, "Verify-Path.ps1");
            File.WriteAllText(scriptPath, """
                $ErrorActionPreference = 'Stop'
                'PF_ACTION_INPUT=' + (Get-Content -LiteralPath './input.txt' -Raw)
                'PF_ACTION_CONTEXT=' + [IO.File]::ReadAllText($env:POWERFORGE_CONTEXT)
                """);
            var contextPath = Path.Combine(root.FullName, "action.context.json");
            File.WriteAllText(contextPath, "{}");
            var operations = new PowerShellModulePipelineHostedOperations(new PowerShellRunner(), new NullLogger());

            var result = operations.RunAction(
                new ModulePipelineActionConfiguration
                {
                    Name = "Read action input",
                    FilePath = filePath ?? scriptPath,
                    WorkingDirectory = workingDirectory ?? buildPath
                },
                CreateActionPathContext(root.FullName), contextPath, root.FullName);

            Assert.True(result.Succeeded, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Equal(scriptPath, result.FilePath);
            Assert.Equal(buildPath, result.WorkingDirectory);
            Assert.Contains("PF_ACTION_INPUT=action input", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("PF_ACTION_CONTEXT={}", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(root.FullName)!.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData(@".\Build")]
    [InlineData(null)]
    public void RunAction_ExecutesInlineInConfiguredDirectoryOrProjectRoot(string? workingDirectory)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var expectedDirectory = workingDirectory is null
                ? root.FullName
                : Directory.CreateDirectory(Path.Combine(root.FullName, "Build")).FullName;
            File.WriteAllText(Path.Combine(expectedDirectory, "input.txt"), "inline input");
            var contextPath = Path.Combine(root.FullName, "action.context.json");
            File.WriteAllText(contextPath, "{}");
            var operations = new PowerShellModulePipelineHostedOperations(new PowerShellRunner(), new NullLogger());

            var result = operations.RunAction(
                new ModulePipelineActionConfiguration
                {
                    Name = "Read inline input",
                    InlineScript = "Get-Content -LiteralPath './input.txt' -Raw -ErrorAction Stop",
                    WorkingDirectory = workingDirectory
                },
                CreateActionPathContext(root.FullName), contextPath, root.FullName);

            Assert.True(result.Succeeded, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.True(result.Inline);
            Assert.Equal(expectedDirectory, result.WorkingDirectory);
            Assert.Contains("inline input", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ModulePipelineActionContext CreateActionPathContext(string projectRoot)
        => new()
        {
            Stage = ModulePipelineActionStage.AfterBuild,
            ActionName = "Read action input",
            ModuleName = "TestModule",
            ProjectRoot = projectRoot
        };
}
