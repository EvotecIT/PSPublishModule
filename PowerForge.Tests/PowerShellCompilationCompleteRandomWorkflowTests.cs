using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedRandomClosurePreservesCallsCapturesAndFailures(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            . "$PSScriptRoot/Get-RandomCharacters.ps1"
            . "$PSScriptRoot/Get-RandomPassword.ps1"
            """, ".psm1");
        foreach (var source in new[] {
            ("Get-RandomCharacters.ps1", "e6a8113feeeddd18398e73c7556125ff92d238610a84ac241e558cb5c14e0a5d"),
            ("Get-RandomPassword.ps1", "36500278fbbe5eff90b8d7a79cf05e51604e83db71a000f8e7e24511aff0edb1") })
        {
            var path = FindCompleteConversionWorkflow("PSSharedGoods", source.Item1);
            Assert.Equal(source.Item2, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            File.Copy(path, Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, source.Item1));
        }
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "Generated.CompleteRandom", resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 2, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        foreach (var name in new[] { "Get-RandomCharacters", "Get-RandomPassword" })
        {
            var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, item => item.Name == name);
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
            Assert.True(unit.RuntimeCommandRegions > 0);
        }
        var observer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShellCompilationRandomWorkflow", "Observe.ps1"));
        var original = RunStatementErrorProbe(host, "$module=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "' -PassThru; " + observer,
            fixture.RootPath, "original-random-workflow");
        var compiled = RunStatementErrorProbe(host, "$module=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "' -PassThru; " + observer,
            fixture.RootPath, "compiled-random-workflow");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(289, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Contains("later:abc", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
        VerifyCompleteRandomCancellation(fixture, result.ArtifactPath!, host);
    }

    private static void VerifyCompleteRandomCancellation(ArtifactFixture fixture, string artifactPath, string host)
    {
        var observer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PowerShellCompilationRandomWorkflow", "Cancellation.ps1"));
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + observer,
            fixture.RootPath, "original-random-cancellation");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(artifactPath) + "'; " + observer,
            fixture.RootPath, "compiled-random-cancellation");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(4, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(2, original.StandardOutput.Split('\n').Count(line => line.Contains("\"state\":\"Stopped\",\"calls\":1,\"cleanups\":1", StringComparison.Ordinal)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
