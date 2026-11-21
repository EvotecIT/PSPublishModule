using System.Text;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    public async Task StrictSourceDecodingMatchesNativeFileInvocationOnBothPinnedHosts()
    {
        const string source = "return 'café'";
        var encodings = new[] { Encoding.Latin1, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) };
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            foreach (var encoding in encodings)
            {
                using var fixture = OracleFixture.Create(source);
                File.WriteAllText(fixture.ScriptPath, source, encoding);
                var observerPath = Path.Combine(fixture.RootPath, "Observe.ps1");
                File.WriteAllText(observerPath, "[Console]::OutputEncoding = [Text.Encoding]::UTF8; & $args[0]");
                var host = StrictStreamingHostPath(pin) ??
                    Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var native = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", observerPath, fixture.ScriptPath }, string.Empty);
                Assert.Equal(0, native.Code);
                Assert.Empty(native.Error);
                if (encoding.GetPreamble().Length != 0) Assert.Equal("café", native.Output.TrimEnd());

                var compiled = BuildStrictStreamingFixture(fixture, "StrictSourceEncoding", pin.ProfileId);
                Assert.True(compiled.Succeeded, compiled.Error + Environment.NewLine + compiled.BuildOutput);
                Assert.False(compiled.Manifest!.RequiresPowerShellRuntime);
                var observation = await ObserveExitedConsole(compiled.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty);
                Assert.Equal(native, observation);
            }
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictDotSourceClosureUsesTheSelectedProfileEncoding()
    {
        const string source = """
            . "$PSScriptRoot/café.ps1"
            Get-Value
            """;
        using var fixture = OracleFixture.Create(source);
        File.WriteAllText(fixture.ScriptPath, source, Encoding.Latin1);
        var legacyCodePage = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8;
        var suffix = new byte[] { 0xe9 };
        var legacyName = "caf" + legacyCodePage.GetString(suffix) + ".ps1";
        var modernName = "caf" + Encoding.UTF8.GetString(suffix) + ".ps1";
        File.WriteAllText(Path.Combine(fixture.RootPath, modernName), "function Get-Value { return 20 }");
        if (!string.Equals(legacyName, modernName, StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(Path.Combine(fixture.RootPath, legacyName), "function Get-Value { return 10 }");

        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var input = new PowerShellCompilationInputResolver(pin.ProfileId).Resolve(fixture.ScriptPath,
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict);
            Assert.Equal(2, input.CompilationSourceFiles.Length);
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                input.SourcePath, Path.Combine(fixture.RootPath, "strict"), "StrictEncodedDotSource",
                input.Kind, input.Mode, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = "net10.0",
                SemanticProfileId = pin.ProfileId,
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles,
                SingleFile = false
            });
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.True(build.Manifest!.DependencyClosureVerified);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, string.Empty);
            Assert.Equal(0, native.Code);
            Assert.Empty(native.Error);
            Assert.Equal(native, await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty));
        }
    }
}
