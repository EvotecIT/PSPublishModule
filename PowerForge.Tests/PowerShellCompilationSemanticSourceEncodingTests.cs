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
}
