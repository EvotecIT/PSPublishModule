using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictBinaryModuleCompilesCrossFileValidatedFunctionGraph()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierValue { [CmdletBinding()] param([int] $Value) return Get-ValidatedValue -InputValue $Value }",
            ".psm1");
        var helper = Path.Combine(fixture.RootPath, "Private.Helper.ps1");
        File.WriteAllText(
            helper,
            "function Get-ValidatedValue { [CmdletBinding()] param([ValidateRange(1, 5)] [int] $InputValue) return $InputValue }");
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(
            PowerShellCompilationMode.Strict,
            new[] { fixture.ScriptPath, helper },
            fixture.RootPath,
            "net10.0",
            PowerShellCompilationCapability.PowerShellStreams | PowerShellCompilationCapability.LocalFunctionCalls);
        Assert.True(
            plan.CanProceed,
            string.Join(Environment.NewLine, plan.Files.SelectMany(file => file.Diagnostics.Concat(file.Units.SelectMany(unit => unit.Diagnostics))).Select(diagnostic => diagnostic.Message)));
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath, helper },
            "PowerForge.TypedFunctionGraph",
            "CompiledPowerShell",
            "net10.0");
        Assert.True(
            typed.Methods.Length == 2,
            string.Join(Environment.NewLine, typed.Diagnostics.Select(diagnostic => diagnostic.Message)));

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedFunctionGraph",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper },
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal("3", RunModuleProof(result.ArtifactPath!, "Get-FrontierValue -Value 3"));
        var invalid = RunModuleFailureProof(result.ArtifactPath!, "Get-FrontierValue -Value 8");
        Assert.NotEqual(0, invalid.ExitCode);
        Assert.Contains("outside its validation range", invalid.StandardError, StringComparison.OrdinalIgnoreCase);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("Get_ValidatedValue", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-ValidatedValue -InputValue", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleEnforcesArrayValidationAcrossTypedLocalCall()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-ValidatedValues { [CmdletBinding()] param([string[]] $Values) return Get-ValidatedLength -Values $Values }",
            ".psm1");
        var helper = Path.Combine(fixture.RootPath, "Private.ArrayValidation.ps1");
        File.WriteAllText(
            helper,
            "function Get-ValidatedLength { [CmdletBinding()] param([ValidateNotNullOrEmpty()] [string[]] $Values) return $Values.Length }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedArrayValidation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("1", RunModuleProof(result.ArtifactPath!, "Invoke-ValidatedValues -Values @('ok')"));
        var empty = RunModuleFailureProof(result.ArtifactPath!, "Invoke-ValidatedValues -Values @()");
        Assert.NotEqual(0, empty.ExitCode);
        Assert.Contains("null or empty", empty.StandardError, StringComparison.OrdinalIgnoreCase);
        var nullCase = RunModuleFailureProof(result.ArtifactPath!, "Invoke-ValidatedValues -Values $null");
        Assert.NotEqual(0, nullCase.ExitCode);
        Assert.Contains("null or empty", nullCase.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StrictBinaryModuleEnforcesObjectArrayElementValidationAcrossTypedLocalCalls()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NotNullLength { [CmdletBinding()] param([ValidateNotNull()] [object[]] $Values) return $Values.Length }; " +
            "function Get-NotEmptyLength { [CmdletBinding()] param([ValidateNotNullOrEmpty()] [object[]] $Values) return $Values.Length }; " +
            "function Invoke-NullElement { [CmdletBinding()] param([AllowNull()] [object[]] $Values) return Get-NotNullLength -Values $Values }; " +
            "function Invoke-EmptyElement { [CmdletBinding()] param([AllowEmptyString()] [object[]] $Values) return Get-NotEmptyLength -Values $Values }; " +
            "Export-ModuleMember -Function Get-NotNullLength, Get-NotEmptyLength, Invoke-NullElement, Invoke-EmptyElement",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedObjectArrayValidation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var nullElement = RunModuleFailureProof(result.ArtifactPath!, "Invoke-NullElement -Values @([object] 1, $null)");
        Assert.NotEqual(0, nullElement.ExitCode);
        Assert.Contains("does not allow null values", nullElement.StandardError, StringComparison.OrdinalIgnoreCase);
        var emptyElement = RunModuleFailureProof(result.ArtifactPath!, "Invoke-EmptyElement -Values @([object] 'ok', '')");
        Assert.NotEqual(0, emptyElement.ExitCode);
        Assert.Contains("does not allow null or empty values", emptyElement.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsEmptyMandatoryStringAcrossTypedLocalCall()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-RequiredName { param([string] $Name) return Get-RequiredName -Name $Name }",
            ".psm1");
        var helper = Path.Combine(fixture.RootPath, "Private.RequiredName.ps1");
        File.WriteAllText(
            helper,
            "function Get-RequiredName { param([Parameter(Mandatory)] [string] $Name) return $Name }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedMandatoryString",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("Ada", RunModuleProof(result.ArtifactPath!, "Invoke-RequiredName -Name Ada"));
        var empty = RunModuleFailureProof(result.ArtifactPath!, "Invoke-RequiredName -Name ''");
        Assert.NotEqual(0, empty.ExitCode);
        Assert.Contains("empty string", empty.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StrictExecutableMutatesOrderedStringDictionary()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Key, [string] $Value); $lookup = [ordered]@{ Alpha = 'one' }; $lookup[$Key] = $Value; return $lookup[$Key]");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.OrderedDictionaryMutation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!, "--Key=BETA", "--Value=two");
        Assert.Equal((0, "two", string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModuleNormalizesNullableStringBeforeIndexing()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierCharacter { param([string] $Key) $lookup = @{ Known = 'value' }; " +
            "$value = $lookup[$Key]; return $value[0] }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableStringIndex",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("v", RunModuleProof(result.ArtifactPath!, "Get-FrontierCharacter -Key Known"));
        Assert.Equal(string.Empty, RunModuleProof(result.ArtifactPath!, "Get-FrontierCharacter -Key Missing"));
    }

    [Fact]
    public void Build_StrictBinaryModuleReadsAndMutatesIDictionaryParameter()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-FrontierHeader { [CmdletBinding()] param([System.Collections.IDictionary] $Headers, [string] $Name, [string] $Value) " +
            "$Headers[$Name] = $Value; return $Headers[$Name] }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.IDictionaryParameter",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "$headers = @{}; Set-FrontierHeader -Headers $headers -Name X-Test -Value two; $headers['X-Test']");
        Assert.Equal(new[] { "two", "two" }, output.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictExecutableCompilesOrderedTypedCatchClauses()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Text); try { return [int]::Parse($Text) } " +
            "catch [System.FormatException] { return -1 } catch [System.OverflowException] { return -2 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedCatch",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var valid = RunProcess(result.ArtifactPath!, "--Text=42");
        var format = RunProcess(result.ArtifactPath!, "--Text=nope");
        var overflow = RunProcess(result.ArtifactPath!, "--Text=999999999999999999999999");
        Assert.Equal((0, "42"), (valid.ExitCode, valid.StandardOutput.Trim()));
        Assert.Equal((0, "-1"), (format.ExitCode, format.StandardOutput.Trim()));
        Assert.Equal((0, "-2"), (overflow.ExitCode, overflow.StandardOutput.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModuleCapturesTypedLocalsInCommandRegion()
    {
        using var fixture = ArtifactFixture.Create(
            "function Write-FrontierValue { [CmdletBinding()] param([int] $Value) [int] $captured = $Value; Write-Output $captured }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedLocalRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("7", RunModuleProof(result.ArtifactPath!, "Write-FrontierValue -Value 7"));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("new object?[] { captured }", generated, StringComparison.Ordinal);
        Assert.Contains("param(${captured})", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesSwitchParameterInsideCommandRegion()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierSwitch { param([switch] $Force) Write-Output $Force.IsPresent }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("False", RunModuleProof(result.ArtifactPath!, "Get-FrontierSwitch"));
        Assert.Equal("True", RunModuleProof(result.ArtifactPath!, "Get-FrontierSwitch -Force"));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("param([switch] ${Force})", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridBinaryModuleCoalescesCommandResultAndDependentTail()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-FrontierFallback { [CmdletBinding()] param([System.Collections.IDictionary] $Headers) " +
            "process { [PSCustomObject]@{ Value = $Headers['X-Test'] } } }; " +
            "function Get-FrontierTail { param([System.Collections.IDictionary] $Headers) $Uri = 'unused'; " +
            "$Output = Invoke-FrontierFallback -Headers $Headers; $Output }; Export-ModuleMember -Function Get-FrontierTail",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommandTail",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(
            new[] { "two", "True", "True" },
            RunModuleProof(
                result.ArtifactPath!,
                "$headers = @{ 'X-Test' = 'two' }; " +
                "(Get-FrontierTail -Headers $headers).Value; " +
                "$hostType = [AppDomain]::CurrentDomain.GetAssemblies().GetTypes() | " +
                "Where-Object Name -Like '*PowerShellRegionHost' | Select-Object -First 1; " +
                "$runspaceId = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId; " +
                "$getDispatcher = $hostType.GetMethod('GetDispatcher'); " +
                "$null -ne $getDispatcher.Invoke($null, @($runspaceId)); " +
                "Remove-Module -Name 'PowerForge.CommandTail'; " +
                "$null -eq $getDispatcher.Invoke($null, @($runspaceId))")
            .Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("$Output = Invoke-FrontierFallback", generated, StringComparison.Ordinal);
        Assert.Contains("new object?[] { Headers }", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridBinaryModuleDoesNotCoalesceTailThatMutatesTypedPrefixLocal()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierMutation { [int] $value = 1; $Output = Get-Date; $value = 2; return $value }; " +
            "Export-ModuleMember -Function Get-FrontierMutation",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommandTailMutation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal("2", RunModuleProof(result.ArtifactPath!, "Get-FrontierMutation"));
    }

    [Fact]
    public void Build_HybridBinaryModuleKeepsRecursiveFunctionCycleOnFallbackPath()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierEven { param([int] $Value) if ($Value -le 0) { return $true }; " +
            "return Get-FrontierOdd -Value ($Value - 1) }; " +
            "function Get-FrontierOdd { param([int] $Value) if ($Value -le 0) { return $false }; " +
            "return Get-FrontierEven -Value ($Value - 1) }; " +
            "Export-ModuleMember -Function Get-FrontierEven",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RecursiveGraph",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(2, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal("True", RunModuleProof(result.ArtifactPath!, "Get-FrontierEven -Value 4"));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunModuleFailureProof(string modulePath, string command)
    {
        var escapedModulePath = modulePath.Replace("'", "''", StringComparison.Ordinal);
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; Import-Module -Name '{escapedModulePath}' -Force; {command}");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Binary module failure proof did not exit within 60 seconds.");
        return (process.ExitCode, output.Trim(), error.Trim());
    }
}
