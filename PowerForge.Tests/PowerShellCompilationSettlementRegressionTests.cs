using System.Globalization;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Fact]
    public void Build_PackagedExecutableRejectsExecutionContextInvokeCommand()
    {
        using var fixture = ArtifactFixture.Create(
            "return $ExecutionContext.InvokeCommand.InvokeScript('$Host.Name')");

        var result = BuildExecutable(
            fixture,
            "PowerForge.InvokeCommandHost",
            PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("PSHost", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModulePreservesDiscoveryInsideDotInvokedScriptBlock()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:before = . { [bool](Get-Command Get-PowerForgeDotLater -ErrorAction SilentlyContinue) }; " +
            "function Get-PowerForgeDotLater { return 1 }; function Get-DotBefore { return $script:before }; " +
            "Export-ModuleMember -Function Get-PowerForgeDotLater, Get-DotBefore",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DotDiscovery",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-DotBefore");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void CensusMethodIdentityComparerNeverTreatsCompositeKeyAsAPath()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var comparer = PowerShellCompilationCensusRunner.CreateMethodIdentityComparer(fixture.ScriptPath);
        var key = Path.GetFullPath(fixture.ScriptPath) + "\0Get-Value";

        var hash = comparer.GetHashCode(key);

        Assert.Equal(hash, comparer.GetHashCode(key));
        Assert.True(comparer.Equals(key, key));
    }

    [Fact]
    public void ManifestMutationPreservesBomlessAnsiMetadata()
    {
        using var fixture = ArtifactFixture.Create("'unused'");
        var manifest = Path.Combine(fixture.RootPath, "Demo.psd1");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        File.WriteAllBytes(
            manifest,
            ansi.GetBytes("@{ RootModule = 'Demo.psm1'; Description = 'Café metadata' }"));

        Assert.True(ManifestEditor.TrySetTopLevelString(manifest, "RootModule", "Compiled.dll"));

        var updated = File.ReadAllText(manifest, new UTF8Encoding(false, true));
        Assert.Contains("Description = 'Café metadata'", updated, StringComparison.Ordinal);
        Assert.Contains("RootModule = 'Compiled.dll'", updated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("DateTime")]
    [InlineData("char")]
    public void Analyze_RejectsNumericValidateRangeOnNonnumericParameter(string typeName)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-RangedValue {{ param([ValidateRange(1, 10)][{typeName}] $Value) return $Value }}");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var unit = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax &&
            diagnostic.Message.Contains("ValidateRange", StringComparison.OrdinalIgnoreCase));
    }
}
