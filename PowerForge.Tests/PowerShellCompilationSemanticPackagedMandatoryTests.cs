namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task RetainedPackagedEntriesBindMandatoryParametersWithoutPrompting()
    {
        using var fixture = OracleFixture.Create("""
            param([Parameter(Mandatory=$true)][Alias('Label')][string]$Name)
            function Get-Number { return 7 }
            "value=$Name|$(Get-Number)"
            """);
        var service = new PowerShellCompilationHostService();
        foreach (var mode in new[] { PowerShellCompilationMode.Package, PowerShellCompilationMode.Hybrid })
        {
            var output = Path.Combine(fixture.RootPath, mode.ToString());
            var check = service.Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath)
            {
                Mode = mode,
                OutputDirectory = output
            });
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, output, "MandatoryParameter",
                PowerShellCompilationArtifactKind.Executable, mode);
            var build = service.Compile(spec, check.ProposedReview!);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

            var successArguments = new[] { "-Label", "Ada" };
            var packagedSuccess = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, successArguments, string.Empty);
            var packagedMissing = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty);
            Assert.True(packagedMissing.Code == 1, $"{mode} omitted argument exit {packagedMissing.Code}: {packagedMissing.Error}");
            Assert.Empty(packagedMissing.Output);
            Assert.Contains("missingmandatoryparameters:Name", string.Concat(packagedMissing.Error.Where(character => !char.IsWhiteSpace(character))),
                StringComparison.OrdinalIgnoreCase);
            foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
            {
                var host = StrictStreamingHostPath(pin) ??
                    Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var originalArguments = new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath };
                var originalSuccess = await ObserveExitedConsole(host, fixture.RootPath,
                    originalArguments.Concat(successArguments).ToArray(), string.Empty);
                Assert.Equal("value=Ada|7", originalSuccess.Output.Trim());
                Assert.Equal(originalSuccess, packagedSuccess);
                var originalMissing = await ObserveExitedConsole(host, fixture.RootPath, originalArguments, string.Empty);
                Assert.Equal(originalMissing.Code, packagedMissing.Code);
                Assert.Equal(originalMissing.Output, packagedMissing.Output);
                Assert.Contains("missingmandatoryparameters:Name", string.Concat(originalMissing.Error.Where(character => !char.IsWhiteSpace(character))),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
