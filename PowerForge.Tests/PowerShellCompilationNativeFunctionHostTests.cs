namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandRegions_PreserveInvocationStoragePipelinesAndErrors(string framework, string host)
    {
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var root = Path.GetDirectoryName(project)!;
        var assembly = Path.Combine(root, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        var result = RunProcess(host, "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(root, "NativeCommandRegionProbe.ps1"), "-Assembly", assembly);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        Assert.Contains("Native command-region qualification passed.", result.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeFunctionHost_PreservesMutableBindingStoragePipelineAndLifecycle(string framework, string host)
    {
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var root = Path.GetDirectoryName(project)!;
        var assembly = Path.Combine(root, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        var probe = Path.Combine(root, "NativeFunctionBindingProbe.ps1");
        var result = RunProcess(host, "-NoProfile", "-NonInteractive", "-File", probe, "-Assembly", assembly);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        Assert.Contains("Native function host qualification passed.", result.StandardOutput);
        Assert.Contains("System.Int32|42|seen=42", result.StandardOutput);
    }
}
