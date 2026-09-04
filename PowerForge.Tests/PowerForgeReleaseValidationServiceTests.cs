using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseValidationServiceTests
{
    [Fact]
    public void Run_ProvidesStableReleaseContextAndEnvironment()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            "release-validation-" + Guid.NewGuid().ToString("N")));
        try
        {
            var scriptPath = Path.Combine(root.FullName, "Validate.ps1");
            File.WriteAllText(
                scriptPath,
                """
                $context = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT -Raw | ConvertFrom-Json
                if ($context.ResolvedVersion -ne '4.0.0') { throw 'version' }
                if ($context.ActionName -ne 'Release contract') { throw 'name' }
                if ($context.StagedAssets.Count -ne 2) { throw 'assets' }
                if ($env:POWERFORGE_RELEASE_STAGE -ne 'AfterStaging') { throw 'stage' }
                if ($env:POWERFORGE_RELEASE_VERSION -ne '4.0.0') { throw 'environment version' }
                Write-Output ($context | ConvertTo-Json -Compress)
                """);
            var context = new PowerForgeReleaseValidationContext
            {
                ResolvedVersion = "4.0.0",
                ConfigPath = Path.Combine(root.FullName, "release.json"),
                ProjectRoot = root.FullName,
                StagedAssets = ["module.zip", "cli.zip"]
            };

            var result = new PowerForgeReleaseValidationService(new NullLogger()).Run(
                new PowerForgeReleaseValidationAction
                {
                    Name = "Release contract",
                    FilePath = scriptPath,
                    WorkingDirectory = root.FullName,
                    TimeoutSeconds = 30
                },
                context,
                root.FullName,
                CancellationToken.None);

            Assert.True(result.Succeeded, result.StdErr);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("Release contract", result.Name);
            using var output = JsonDocument.Parse(result.StdOut.Trim());
            Assert.Equal("4.0.0", output.RootElement.GetProperty("ResolvedVersion").GetString());
            Assert.False(File.Exists(context.ContextPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_CapturesValidationFailureWithoutThrowing()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            "release-validation-" + Guid.NewGuid().ToString("N")));
        try
        {
            var scriptPath = Path.Combine(root.FullName, "Reject.ps1");
            File.WriteAllText(scriptPath, "[Console]::Error.WriteLine('invalid staged release'); exit 17");

            var result = new PowerForgeReleaseValidationService(new NullLogger()).Run(
                new PowerForgeReleaseValidationAction
                {
                    FilePath = scriptPath,
                    TimeoutSeconds = 30
                },
                new PowerForgeReleaseValidationContext(),
                root.FullName,
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(17, result.ExitCode);
            Assert.Contains("invalid staged release", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_TimeoutTerminatesValidationProcessTree()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            "release-validation-tree-" + Guid.NewGuid().ToString("N")));
        try
        {
            var markerPath = Path.Combine(root.FullName, "child-survived.txt");
            var childScriptPath = Path.Combine(root.FullName, "Child.ps1");
            var parentScriptPath = Path.Combine(root.FullName, "Parent.ps1");
            File.WriteAllText(
                childScriptPath,
                $"Start-Sleep -Seconds 3{Environment.NewLine}[IO.File]::WriteAllText('{markerPath.Replace("'", "''")}', 'alive')");
            File.WriteAllText(
                parentScriptPath,
                $"Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-File', '{childScriptPath.Replace("'", "''")}') | Out-Null{Environment.NewLine}Start-Sleep -Seconds 30");

            var result = new PowerForgeReleaseValidationService(new NullLogger()).Run(
                new PowerForgeReleaseValidationAction
                {
                    Name = "process tree timeout",
                    FilePath = parentScriptPath,
                    WorkingDirectory = root.FullName,
                    TimeoutSeconds = 1
                },
                new PowerForgeReleaseValidationContext(),
                root.FullName,
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.True(result.TimedOut);
            Thread.Sleep(TimeSpan.FromSeconds(4));
            Assert.False(File.Exists(markerPath), "The validation child process survived the timeout boundary.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
