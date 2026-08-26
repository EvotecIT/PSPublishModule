using System.Globalization;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Fact]
    public void Build_PackagedScriptDependencyUsesExtractedRootForDynamicReference()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/helper.ps1\"; $path = Join-Path $PSScriptRoot 'helper.ps1'; " +
            "\"$(Test-Path -LiteralPath $path)|$(Get-HelperValue)\"");
        var helper = Path.Combine(fixture.RootPath, "helper.ps1");
        File.WriteAllText(helper, "function Get-HelperValue { return 17 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ScriptDependencyRoot",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper },
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal((0, "True|17", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Theory]
    [InlineData("Invoke-Expression '$Host.Name'")]
    [InlineData("iex '$Host.Name'")]
    [InlineData("[scriptblock]::Create('$Host.Name').Invoke()")]
    public void Build_PackagedExecutableRejectsDynamicScriptEvaluation(string source)
    {
        using var fixture = ArtifactFixture.Create(source);

        var result = BuildExecutable(
            fixture,
            "PowerForge.DynamicEvaluation",
            PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("dynamic script evaluation", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("[bool]$ExecutionContext.InvokeCommand.GetCommand('Get-PowerForgeInvokeLater',[System.Management.Automation.CommandTypes]::All)")]
    [InlineData("[bool]@($ExecutionContext.InvokeCommand.GetCommands('Get-PowerForgeInvoke*',[System.Management.Automation.CommandTypes]::All,$false)).Count")]
    [InlineData("& { $discovery = $ExecutionContext.InvokeCommand; [bool]$discovery.GetCommand('Get-PowerForgeInvokeLater',[System.Management.Automation.CommandTypes]::All) }")]
    [InlineData("& { $first = $ExecutionContext.InvokeCommand; $discovery = $first; [bool]@($discovery.GetCommands('Get-PowerForgeInvoke*',[System.Management.Automation.CommandTypes]::All,$false)).Count }")]
    public void Build_HybridModulePreservesInvokeCommandDiscoveryTiming(string discovery)
    {
        using var fixture = ArtifactFixture.Create(
            $"$script:before = {discovery}; " +
            "function Get-PowerForgeInvokeLater { return 1 }; function Get-InvokeBefore { return $script:before }; " +
            "Export-ModuleMember -Function Get-PowerForgeInvokeLater, Get-InvokeBefore",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InvokeCommandDiscovery",
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
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-InvokeBefore");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryCmdletUsesScriptFunctionInvariantDateBinding()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-CultureDate { [CmdletBinding()] param([DateTime] $When) return $When.Year }; " +
            "Export-ModuleMember -Function Get-CultureDate",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InvariantDateBinding",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledCmdlets.cs"));
        Assert.Contains("__PowerForgeInvariantParameterAttribute", generated, StringComparison.Ordinal);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "$old = [Globalization.CultureInfo]::CurrentCulture; try { " +
            "[Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE'); " +
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; " +
            "try { Get-CultureDate -When '19-06-2018'; 'unexpected' } catch { 'rejected' }; " +
            "Get-CultureDate -When '06/19/2018' " +
            "} finally { [Globalization.CultureInfo]::CurrentCulture = $old }");
        Assert.Equal((0, "rejected" + Environment.NewLine + "2018", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

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
