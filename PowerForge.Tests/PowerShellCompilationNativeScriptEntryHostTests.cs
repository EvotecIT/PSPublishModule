namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeScriptEntry_PreservesAuthorizationAndRejectsUnqualifiedSourceAndLanguage(string framework, string host)
    {
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var root = Path.GetDirectoryName(project)!;
        var assembly = Path.Combine(root, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        var result = RunProcess(host, "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(root, "NativeScriptEntryPolicyProbe.ps1"), "-Assembly", assembly);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        Assert.Contains("Native script entry policy comparison passed:", result.StandardOutput);
    }

}
