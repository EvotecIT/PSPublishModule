using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task PackagedEntryPreservesSupportedFileRequirementsOnPinnedHosts()
    {
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");

        foreach (var (requirements, includeDesktop, dependency) in new[]
                 {
                     ("#requires -Version 5.1", true, false),
                     ("#requires -Ver 7.6.99\n#requires -PSEdition Core", false, false),
                     ("#requires -Version 5.1", true, true),
                     ("#requires -Ver 7.6.99\n#requires -PSEdition Core", false, true)
                 })
        {
            const string body = "\n$description = @'\n#requires -Modules ExampleNotARequirement\n'@\n'Supported package entry'";
            const string loader = ". \"$PSScriptRoot/Helper.ps1\"";
            using var fixture = OracleFixture.Create(dependency ? loader : requirements + body);
            if (dependency)
                File.WriteAllText(Path.Combine(fixture.RootPath, "Helper.ps1"), requirements + body);
            var output = Path.Combine(fixture.RootPath, "app");
            var host = new PowerShellCompiler();
            var check = host.Check(new PowerShellCompilationRequest(fixture.ScriptPath)
            {
                Mode = PowerShellCompilationMode.Package,
                OutputDirectory = output
            });
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package);
            var spec = new PowerShellCompilationBuildSpec(input.SourcePath, output, "RequirementProbe", input.Kind, input.Mode)
            {
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles
            };
            var built = host.BuildReviewed(spec, check.ProposedReview!);
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);

            var packaged = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                built.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), TimeSpan.FromSeconds(30)));
            Assert.True(packaged.Succeeded, packaged.StdErr);
            Assert.Equal("Supported package entry", packaged.StdOut.Trim());
            Assert.Empty(packaged.StdErr);
            var profiles = new List<(string Id, string? Executable)>
            {
                (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost)
            };
            if (includeDesktop)
                profiles.Add((PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, null));

            foreach (var (profile, executable) in profiles)
            {
                var original = new PowerShellCompilationSemanticOracleRunner().Observe(
                    new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                    {
                        HostExecutablePath = executable,
                        ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                            PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                    });
                Assert.Empty(original.ErrorRecords);
                Assert.Equal(Assert.Single(original.Success).Value, packaged.StdOut.Trim());
            }
        }
    }
}
