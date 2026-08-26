namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Fact]
    public void Rewrite_PackagedSourceKeepsDecodedUnicodeAlignedWithAstReplacements()
    {
        using var fixture = ArtifactFixture.Create("'zażółć'; return $PSCommandPath");

        var rewritten = PowerShellPackagedScriptRewriter.Rewrite(
            fixture.ScriptPath,
            packagedCommandPathExpression: "'compiled-entry.ps1'");

        Assert.Contains("'zażółć'", rewritten, StringComparison.Ordinal);
        Assert.Contains("'compiled-entry.ps1'", rewritten, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Get-Variable -Name x -ValueOnly")]
    [InlineData("gv -Name x -ValueOnly")]
    [InlineData("Get-Content Variable:x")]
    public void Build_HybridBinaryModuleRoutesLiteralVariableLookupToFallback(string lookup)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-LiteralLocal {{ [int] $x = 1; [int] $y = {lookup}; return $y }}; " +
            "Export-ModuleMember -Function Get-LiteralLocal",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LiteralVariableLookup",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-LiteralLocal");
        Assert.Equal((0, "1", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesNullableValueBoxingForMemberObservation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NullableObservation { param([Nullable[int]] $Value); " +
            "$observed = $Value.HasValue; if ($null -eq $observed) { return 'null' }; return $observed }; " +
            "Export-ModuleMember -Function Get-NullableObservation",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableValueObservation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("boxing semantics", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-NullableObservation -Value 3; Get-NullableObservation");
        Assert.Equal((0, "null" + Environment.NewLine + "null", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesNullArrayTypedCatchRouting()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NullArrayRoute { param([int[]] $Numbers); try { return $Numbers[0] } " +
            "catch [System.InvalidOperationException] { return [object] 'clr' } " +
            "catch { return [object] 'powershell' } }; " +
            "Export-ModuleMember -Function Get-NullArrayRoute",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullArrayRoute",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("new global::System.Management.Automation.RuntimeException", generated, StringComparison.Ordinal);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-NullArrayRoute");
        Assert.Equal((0, "powershell", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictLibraryRejectsNullArrayIndexWhenTypedCatchCanObserveExceptionIdentity()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NullArrayRoute { param([int[]] $Numbers); try { return $Numbers[0] } " +
            "catch [System.InvalidOperationException] { throw } }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullArrayIndependent",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("runtime-error identity", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableRejectsNamedParameterSets()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(DefaultParameterSetName='ByName')] param(" +
            "[Parameter(Mandatory,ParameterSetName='ByName')][string] $Name," +
            "[Parameter(Mandatory,ParameterSetName='ById')][int] $Id); return $Name");
        var result = BuildExecutable(fixture, "PowerForge.NamedSetExecutable", PowerShellCompilationMode.Strict);

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData(
        "[CmdletBinding()] param([Parameter(Position=0)][string] $First,[Parameter(ValueFromRemainingArguments)][string[]] $Rest); return $First + '/' + ($Rest -join '|')",
        "a/b|c")]
    [InlineData(
        "[CmdletBinding(PositionalBinding=$false)] param([Parameter(ValueFromRemainingArguments)][string[]] $Rest); return $Rest -join '|'",
        "a|b|c")]
    public void Build_StrictExecutableBindsAllRemainingPositionalArguments(string source, string expected)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.RemainingArguments", PowerShellCompilationMode.Strict);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, "a", "b", "c");
        Assert.Equal((0, expected, string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictExecutableRejectsScalarRemainingArgumentsJoining()
    {
        using var fixture = ArtifactFixture.Create(
            "param([Parameter(ValueFromRemainingArguments)][string] $Rest); return $Rest");
        var result = BuildExecutable(fixture, "PowerForge.ScalarRemainingArguments", PowerShellCompilationMode.Strict);

        Assert.False(result.Succeeded);
        Assert.Contains("whitespace-joining semantics", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}
