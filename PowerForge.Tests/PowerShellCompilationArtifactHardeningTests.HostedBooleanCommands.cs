using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Transpile_BinaryModuleBindsBoundedHostedBooleanCommands()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-ExistingPath { [CmdletBinding()] param([string] $Path) return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath \"FileSystem::$Path\" -EA Ignore) }; " +
            "function Test-LeafPath { [CmdletBinding()] param([string] $Path) if (Microsoft.PowerShell.Management\\Test-Path -PSPath \"FileSystem::$Path\" -PathType Leaf -ErrorAction Ignore) { return $true }; return $false }; " +
            "function Test-ValidPath { [CmdletBinding()] param() return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath 'FileSystem::proof' -IsValid -ErrorAction Ignore) }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.HostedBooleanModule",
            "CompiledPowerShell",
            "net10.0");

        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        Assert.Equal(3, typed.Methods.Length);
        Assert.All(typed.Methods, static method =>
        {
            Assert.True(method.RequiresPowerShellCommandRegions);
            Assert.False(method.RequiresPowerShellStreams);
            Assert.False(method.RequiresPowerShellRuntimeState);
            Assert.Equal(1, method.HostedRegionSiteCount);
            var provider = Assert.Single(method.CommandProviders);
            Assert.Equal("powerforge.command.hosted-boolean.test-path", provider.ProviderId);
            Assert.Equal(PowerShellCompilationCommandFamily.HostedBooleanQuery, provider.Family);
            Assert.False(provider.Adapter.RuntimeFree);
        });
        Assert.Contains("Microsoft.PowerShell.Management\\\\Test-Path", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("-LiteralPath $__pfArg0 -PathType $__pfArg1", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("-LiteralPath $__pfArg0 -IsValid -ErrorAction $__pfArg1", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("LanguagePrimitives.IsTrue(__invokePowerShellCapture", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_HostedBooleanCommandsRejectUnboundedShapesAndRuntimeFreeTargets()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-DynamicPathType { param([string] $Path, [string] $Kind) return [bool](Test-Path -LiteralPath \"FileSystem::$Path\" -PathType $Kind -EA Ignore) }; " +
            "function Test-DynamicPath { param([string] $Path) return [bool](Test-Path -LiteralPath $Path -EA Ignore) }; " +
            "function Test-MultiplePaths { param([string[]] $Path) return [bool](Test-Path -LiteralPath $Path -EA Ignore) }; " +
            "function Test-WildcardPathSet { return [bool](Test-Path -Path 'FileSystem::*' -EA Ignore) }; " +
            "function Test-LocalVariableProviderPath { [string] $Present = 'yes'; return [bool](Test-Path -LiteralPath 'Variable:\\Present' -EA Ignore) }; " +
            "function Test-LocalFunctionProviderPath { return [bool](Test-Path -LiteralPath 'Function:\\Get-Local' -EA Ignore) }; " +
            "function Test-LocalAliasProviderPath { return [bool](Test-Path -LiteralPath 'Alias:\\local' -EA Ignore) }; " +
            "function Test-QualifiedVariableProviderPath { return [bool](Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Variable::Present' -EA Ignore) }; " +
            "function Test-QualifiedFunctionProviderPath { return [bool](Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Function::Get-Local' -EA Ignore) }; " +
            "function Test-QualifiedAliasProviderPath { return [bool](Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Alias::local' -EA Ignore) }; " +
            "function Test-MissingErrorPolicy { return [bool](Test-Path -LiteralPath 'FileSystem::proof') }; " +
            "function Test-CrossProfileAlias { return [bool](Test-Path -LP 'FileSystem::proof' -EA Ignore) }; " +
            "function Test-RedirectedPath { return [bool](Test-Path -LiteralPath 'FileSystem::proof' -EA Ignore 2>$null) }",
            ".psm1");

        var hybrid = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,
            capabilities: PowerShellCompilationCapability.PowerShellStreams |
                          PowerShellCompilationCapability.PowerShellHostTypes |
                          PowerShellCompilationCapability.BoundParameters));

        Assert.All(Assert.Single(hybrid.Files).Units, static unit =>
            Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == "command.test-path"));

        using var strictFixture = ArtifactFixture.Create(
            "function Test-ExistingPath { return [bool](Test-Path -LiteralPath 'FileSystem::proof' -EA Ignore) }",
            ".psm1");
        var strict = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            strictFixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            capabilities: PowerShellCompilationCapability.None));

        var strictUnit = Assert.Single(Assert.Single(strict.Files).Units);
        Assert.Contains(strictUnit.Diagnostics, static diagnostic => diagnostic.FeatureId == "command.test-path");
    }

    [Fact]
    public void Transpile_AndStrictLibraryRejectHostedBooleanCommandsBeforeSourceEmission()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-ExistingPath { return [bool](Test-Path -LiteralPath 'FileSystem::proof' -EA Ignore) }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, static diagnostic => diagnostic.FeatureId == "command.test-path");
        Assert.DoesNotContain("__invokePowerShellCapture", typed.SourceCode, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", typed.SourceCode, StringComparison.Ordinal);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeFreeHostedBoolean",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactPath);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Contains("No PowerShell functions were eligible", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LocalProviderStatePathsRemainHostedAcrossPinnedWindowsProfiles()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-LocalProviderState { [CmdletBinding()] param() [string] $Present = 'yes'; " +
            "return [bool](Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Variable::Present' -EA Ignore) }",
            ".psm1");
        var analysis = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.Contains(Assert.Single(Assert.Single(analysis.Files).Units).Diagnostics,
            static diagnostic => diagnostic.FeatureId == "command.test-path");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        const string proof = "& { $Present = 'yes'; function Test-ProviderForms { }; Set-Alias local Test-ProviderForms; " +
                             "@((Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Variable::Present' -EA Ignore)," +
                             "(Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Function::Test-ProviderForms' -EA Ignore)," +
                             "(Test-Path -LiteralPath 'Microsoft.PowerShell.Core\\Alias::local' -EA Ignore)) -join '|' }";
        var windowsPowerShell = Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", proof);
        var powerShell = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", proof);
        Assert.Equal((0, "True|True|True"), (windowsPowerShell.ExitCode, windowsPowerShell.StandardOutput.Trim()));
        Assert.Equal((0, "True|True|True"), (powerShell.ExitCode, powerShell.StandardOutput.Trim()));
    }

    [Fact]
    public void Build_BinaryModulePropagatesBoundParameterPresenceFromHostedArguments()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-BoundPath { [CmdletBinding()] param([string] $Optional) " +
            "return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath ('FileSystem::' + [string]$PSBoundParameters.ContainsKey('Optional')) -EA Ignore) }; " +
            "function Test-NestedBoundPath { [CmdletBinding(SupportsShouldProcess)] param([string] $Optional) " +
            "return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath ('FileSystem::' + [string]$PSCmdlet.ShouldProcess([string]$PSBoundParameters.ContainsKey('Optional'))) -EA Ignore) }; " +
            "function Test-DiscoveredBoundName { [CmdletBinding()] param([string] $Optional) " +
            "return [bool](Microsoft.PowerShell.Core\\Get-Command -Name ([string]$PSBoundParameters.ContainsKey('Optional')) -EA Ignore) }; " +
            "function Test-HostedShouldProcess { [CmdletBinding(SupportsShouldProcess)] param([string] $Optional) " +
            "return $PSCmdlet.ShouldProcess([string][bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath ('FileSystem::' + [string]$PSBoundParameters.ContainsKey('Optional')) -EA Ignore)) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HostedBooleanBoundParameters",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net8.0",
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        var methods = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.HostedBooleanBoundParameters",
            "CompiledPowerShell",
            "net8.0").Methods;
        Assert.Equal(4, methods.Length);
        Assert.All(methods, static method => Assert.True(method.RequiresPowerShellBoundParameters));
        Assert.True(Assert.Single(methods, static method => method.SourceName == "Test-NestedBoundPath").RequiresPowerShellRuntimeState);
        var nestedHosted = Assert.Single(methods, static method => method.SourceName == "Test-HostedShouldProcess");
        Assert.True(nestedHosted.RequiresPowerShellRuntimeState);
        Assert.True(nestedHosted.RequiresPowerShellCommandRegions);
        Assert.Equal(1, nestedHosted.HostedRegionSiteCount);
        Assert.Contains(nestedHosted.CommandProviders, static provider => provider.ProviderId == "powerforge.command.hosted-boolean.test-path");
        Assert.Contains("__boundParameters", File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridModuleRetainsBasicHostedBooleanFunction()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-BasicPath { return [bool](Test-Path -LiteralPath 'FileSystem::missing' -EA Ignore) }; " +
            "Export-ModuleMember -Function Test-BasicPath",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HostedBooleanBasicFunction",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
    }

    [Fact]
    public void Analyze_CrossProfileTestPathContractRejectsPowerShellSevenOnlyAlias()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Alias { [CmdletBinding()] param() return [bool](Test-Path -LP 'FileSystem::proof' -EA Ignore) }",
            ".psm1");
        var analysis = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.Contains(Assert.Single(Assert.Single(analysis.Files).Units).Diagnostics,
            static diagnostic => diagnostic.FeatureId == "command.test-path");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var windowsPowerShell = Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            "Microsoft.PowerShell.Management\\Test-Path -LP 'C:\\'");
        var powerShell = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "Microsoft.PowerShell.Management\\Test-Path -LP 'C:\\'");
        Assert.NotEqual(0, windowsPowerShell.ExitCode);
        Assert.Contains("NamedParameterNotFound", windowsPowerShell.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, "True"), (powerShell.ExitCode, powerShell.StandardOutput.Trim()));
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesHostedTestPathSemantics(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Test-ExistingPath { [CmdletBinding()] param([string] $TargetLocation) return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath \"FileSystem::$TargetLocation\" -EA Ignore) }; " +
            "function Test-LeafPath { [CmdletBinding()] param([string] $TargetLocation) return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath \"FileSystem::$TargetLocation\" -PathType Leaf -ErrorAction Ignore) }; " +
            "function Test-ContainerPath { [CmdletBinding()] param([string] $TargetLocation) if (Microsoft.PowerShell.Management\\Test-Path -LiteralPath \"FileSystem::$TargetLocation\" -PathType Container -EA Ignore) { return $true }; return $false }; " +
            "function Test-ValidPath { [CmdletBinding()] param([string] $TargetLocation) return [bool](Microsoft.PowerShell.Management\\Test-Path -LiteralPath \"FileSystem::$TargetLocation\" -IsValid -EA Ignore) }",
            ".psm1");
        var existingFile = Path.Combine(fixture.RootPath, "value.txt");
        File.WriteAllText(existingFile, "value");
        var missingFile = Path.Combine(fixture.RootPath, "missing.txt");
        var wildcardPath = Path.Combine(fixture.RootPath, "*.txt");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HostedBooleanModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var provider = Assert.Single(result.Manifest!.CommandProviders);
        Assert.Equal("powerforge.command.hosted-boolean.test-path", provider.ProviderId);
        Assert.True(result.Manifest.RequiresPowerShellRuntime);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        Assert.All(ledger.Entries, static entry =>
        {
            Assert.Equal(1, entry.RuntimeCommandRegions);
            Assert.Equal(1, entry.BoundaryCrossings);
        });

        var calls =
            $"@(" +
            $"(Test-ExistingPath '{existingFile.Replace("'", "''", StringComparison.Ordinal)}')," +
            $"(Test-ExistingPath '{missingFile.Replace("'", "''", StringComparison.Ordinal)}' -ErrorVariable pathError)," +
            $"($pathError.Count)," +
            $"(Test-ExistingPath '{wildcardPath.Replace("'", "''", StringComparison.Ordinal)}')," +
            $"(Test-LeafPath '{existingFile.Replace("'", "''", StringComparison.Ordinal)}')," +
            $"(Test-LeafPath '{fixture.RootPath.Replace("'", "''", StringComparison.Ordinal)}')," +
            $"(Test-LeafPath '{wildcardPath.Replace("'", "''", StringComparison.Ordinal)}')," +
            $"(Test-ContainerPath '{fixture.RootPath.Replace("'", "''", StringComparison.Ordinal)}')," +
            $"(Test-ValidPath '{existingFile.Replace("'", "''", StringComparison.Ordinal)}')) -join '|'";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.Equal("True|False|0|False|True|False|False|True|True", compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }
}
