using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_StrictExecutableLowersTargetAndPlatformStateWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "return (($PSEdition -eq 'Core') -and $IsCoreCLR -and ($IsWindows -or $IsLinux -or $IsMacOS))");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        var run = Run(result.ArtifactPath!, Array.Empty<string>());
        Assert.Equal((0, "True", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
    }

    [Theory]
    [InlineData(PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, "net472", 5)]
    [InlineData(PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId, "net8.0", 7)]
    [InlineData(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, "net10.0", 7)]
    public void Transpile_VersionMajorIsFixedBySemanticProfileWithoutHostState(
        string profileId,
        string targetFramework,
        int expectedMajor)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-VersionMajor { return $PSVersionTable.PSVersion.Major }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler(
            Array.Empty<PowerShellCompilationCommandProviderContract>(),
            profileId).Transpile(
                fixture.ScriptPath,
                "PowerForge.VersionMajor",
                "CompiledPowerShell",
                targetFramework);

        var method = Assert.Single(typed.Methods);
        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        Assert.False(method.RequiresPowerShellRuntimeState);
        Assert.Contains($"return {expectedMajor};", typed.SourceCode, StringComparison.Ordinal);

        var binaryModule = new PowerShellTypedCompilationTranspiler(
            Array.Empty<PowerShellCompilationCommandProviderContract>(),
            profileId).TranspileForBinaryModule(
                new[] { fixture.ScriptPath },
                "PowerForge.VersionMajor",
                "CompiledPowerShell",
                targetFramework);
        Assert.False(Assert.Single(binaryModule.Methods).RequiresPowerShellRuntimeState);
    }

    [Fact]
    public void Build_StrictExecutableLowersProcessUserAndCultureStateWithoutPowerShellRuntime()
    {
        const string caseId = "PowerForge.Semantic/runtime-process-user-culture-state";
        using var fixture = ArtifactFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(caseId));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ProcessUserCultureStateExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, Array.Empty<string>());
        Assert.Equal((0, "True", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
    }

    [Fact]
    public void Build_StrictExecutableReadsOneBoundedEnvironmentValueWithoutPowerShellRuntime()
    {
        const string variable = "POWERFORGE_RUNTIME_STATE_PROOF";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "bounded-environment");
            using var fixture = ArtifactFixture.Create("return $env:POWERFORGE_RUNTIME_STATE_PROOF");
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.EnvironmentStateExecutable",
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = "net10.0"
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            var run = Run(result.ArtifactPath!, Array.Empty<string>());
            Assert.Equal((0, "bounded-environment", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
            Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Transpile_BinaryModuleReadsEnvironmentValuesInsideControlFlow()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-EnvironmentState { param([string] $InvocationName) " +
            "if ($InvocationName -ne '.') { return $true }; " +
            "if (-not $env:CI) { return $false }; " +
            "[bool] $should = $true; " +
            "if ($null -ne $env:POWERFORGE_NOINSTALL) { $should = $false }; " +
            "return $should }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.EnvironmentStateModule",
            "CompiledPowerShell",
            "net10.0");

        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        var method = Assert.Single(typed.Methods);
        Assert.Contains("global::System.Environment.GetEnvironmentVariable(\"CI\")", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("global::System.Environment.GetEnvironmentVariable(\"POWERFORGE_NOINSTALL\")", typed.SourceCode, StringComparison.Ordinal);
        Assert.False(method.RequiresPowerShellRuntimeState);
    }

    [Fact]
    public void Transpile_BinaryModuleBindsOnlyBooleanGetCommandDiscovery()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-CommandAvailability { param([string] $Name) return [bool](Microsoft.PowerShell.Core\\Get-Command $Name -ErrorAction SilentlyContinue) }; " +
            "function Test-LiteralCommandAvailability { if (Microsoft.PowerShell.Core\\Get-Command -Name Get-Command -EA Ignore) { return $true }; return $false }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.CommandDiscoveryModule",
            "CompiledPowerShell",
            "net10.0");

        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        Assert.Equal(2, typed.Methods.Length);
        Assert.All(typed.Methods, static method =>
        {
            Assert.True(method.RequiresPowerShellCommandRegions);
            Assert.False(method.RequiresPowerShellRuntimeState);
            Assert.Equal(1, method.HostedRegionSiteCount);
        });
        Assert.Contains("__invokePowerShellCapture", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("Microsoft.PowerShell.Core\\\\Get-Command", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("\"SilentlyContinue\"", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("\"Ignore\"", typed.SourceCode, StringComparison.Ordinal);
        Assert.DoesNotContain("__commandAvailable", typed.SourceCode, StringComparison.Ordinal);
        var providers = typed.Methods.SelectMany(static method => method.CommandProviders).ToArray();
        Assert.All(providers, static provider =>
        {
            Assert.Equal("powerforge.command.discovery.get-command", provider.ProviderId);
            Assert.Equal(PowerShellCompilationCommandFamily.CommandDiscovery, provider.Family);
            Assert.False(provider.Adapter.RuntimeFree);
        });
    }

    [Fact]
    public void Transpile_BasicCommandDiscoveryDoesNotPermitOtherHostedRegions()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-MixedCommandHost { [bool] $available = Microsoft.PowerShell.Core\\Get-Command Get-Command -EA Ignore; $ignored = Get-Date -Format o; return $available }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.MixedCommandHost",
            "CompiledPowerShell",
            "net8.0");

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("Basic function", StringComparison.Ordinal));

        const string helper =
            "function Invoke-HostedHelper { [CmdletBinding()] param() $ignored = Get-Date -Format o }; ";
        using var transitiveFixture = ArtifactFixture.Create(
            helper + "function Test-TransitiveAvailability { Invoke-HostedHelper; return [bool](Microsoft.PowerShell.Core\\Get-Command Get-Command -EA Ignore) }",
            ".psm1");
        var transitive = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { transitiveFixture.ScriptPath },
            "PowerForge.TransitiveCommandHost",
            "CompiledPowerShell",
            "net8.0");

        Assert.DoesNotContain(transitive.Methods, static method => method.SourceName == "Test-TransitiveAvailability");
        Assert.Contains(transitive.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("Basic function 'Test-TransitiveAvailability'", StringComparison.Ordinal));

        using var countedFixture = ArtifactFixture.Create(
            helper + "function Test-CountedAvailability { [CmdletBinding()] param() Invoke-HostedHelper; return [bool](Microsoft.PowerShell.Core\\Get-Command Get-Command -EA Ignore) }",
            ".psm1");
        var counted = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { countedFixture.ScriptPath },
            "PowerForge.CountedCommandHost",
            "CompiledPowerShell",
            "net8.0");

        Assert.Empty(counted.Diagnostics.Select(static diagnostic => diagnostic.Message));
        var caller = Assert.Single(counted.Methods, static method => method.SourceName == "Test-CountedAvailability");
        Assert.Equal(2, caller.HostedRegionSiteCount);
    }

    [Fact]
    public void Transpile_CommandDiscoveryNameParticipatesInFlowAndCallGraphAnalysis()
    {
        using var invalidFixture = ArtifactFixture.Create(
            "function Test-UnassignedDiscovery { param([bool] $UseName) if ($UseName) { $Name = 'Get-Command' }; return [bool](Microsoft.PowerShell.Core\\Get-Command $Name -EA Ignore) }",
            ".psm1");
        var invalid = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { invalidFixture.ScriptPath },
            "PowerForge.InvalidCommandDiscovery",
            "CompiledPowerShell",
            "net8.0");

        Assert.Empty(invalid.Methods);
        Assert.Contains(invalid.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("may remain unassigned", StringComparison.OrdinalIgnoreCase));

        using var nestedFixture = ArtifactFixture.Create(
            "function Get-DiscoveryName { [CmdletBinding()] param() Microsoft.PowerShell.Utility\\Write-Verbose 'discovery'; return 'Get-Command' }; " +
            "function Test-NestedCommandAvailability { [CmdletBinding()] param() return [bool](Microsoft.PowerShell.Core\\Get-Command (Get-DiscoveryName) -EA Ignore) }",
            ".psm1");
        var nested = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { nestedFixture.ScriptPath },
            "PowerForge.NestedCommandDiscovery",
            "CompiledPowerShell",
            "net8.0");

        Assert.Empty(nested.Diagnostics.Select(static diagnostic => diagnostic.Message));
        var caller = Assert.Single(nested.Methods, static method => method.SourceName == "Test-NestedCommandAvailability");
        Assert.True(caller.RequiresPowerShellCommandRegions);
        Assert.True(caller.RequiresPowerShellStreams);
        Assert.Contains("Get_DiscoveryName(__writeOutput", nested.SourceCode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesBooleanGetCommandDiscovery(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Test-CommandAvailability { param([string] $Name) return [bool](Microsoft.PowerShell.Core\\Get-Command $Name -ErrorAction SilentlyContinue) }; " +
            "function Test-IgnoreCommandAvailability { param([string] $Name) return [bool](Microsoft.PowerShell.Core\\Get-Command $Name -ErrorAction Ignore) }; " +
            "function Test-LiteralCommandAvailability { if (Microsoft.PowerShell.Core\\Get-Command -Name Get-Command -EA Ignore) { return $true }; return $false }",
            ".psm1");
        var discoveryModuleRoot = Path.Combine(fixture.RootPath, "modules");
        var discoveryModulePath = Path.Combine(discoveryModuleRoot, "PowerForgeCommandDiscoveryFixture");
        Directory.CreateDirectory(discoveryModulePath);
        File.WriteAllText(
            Path.Combine(discoveryModulePath, "PowerForgeCommandDiscoveryFixture.psm1"),
            "function Get-PowerForgeDiscoveryFixture { 'available' }; Export-ModuleMember -Function Get-PowerForgeDiscoveryFixture");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommandDiscoveryModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var provider = Assert.Single(result.Manifest!.CommandProviders);
        Assert.Equal("powerforge.command.discovery.get-command", provider.ProviderId);
        Assert.Equal(PowerShellCompilationCommandFamily.CommandDiscovery, provider.Family);
        Assert.True(result.Manifest.RequiresPowerShellRuntime);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        Assert.All(ledger.Entries, static entry =>
        {
            Assert.Equal(1, entry.RuntimeCommandRegions);
            Assert.Equal(1, entry.BoundaryCrossings);
        });
        var escapedModuleRoot = discoveryModuleRoot.Replace("'", "''", StringComparison.Ordinal);
        var setup = $"$env:PSModulePath = '{escapedModuleRoot}' + [IO.Path]::PathSeparator + $env:PSModulePath; ";
        const string calls =
            "$qualified = Test-CommandAvailability 'PowerForgeCommandDiscoveryFixture\\Get-PowerForgeDiscoveryFixture'; " +
            "Remove-Module PowerForgeCommandDiscoveryFixture -ErrorAction Ignore; " +
            "$exact = Test-CommandAvailability 'Get-PowerForgeDiscoveryFixture'; " +
            "$wildcard = Test-CommandAvailability 'Get-PowerForgeDiscoveryF*'; " +
            "$Error.Clear(); $silent = Test-CommandAvailability 'PowerForge-Definitely-Missing-*'; $silentErrors = $Error.Count; " +
            "$Error.Clear(); $ignored = Test-IgnoreCommandAvailability 'PowerForge-Definitely-Missing-*'; $ignoredErrors = $Error.Count; " +
            "$literal = Test-LiteralCommandAvailability; " +
            "@($qualified, $exact, $wildcard, $silent, $silentErrors, $ignored, $ignoredErrors, $literal) -join '|'";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"{setup} Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"{setup} Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Build_StrictLibraryRejectsGetCommandDiscovery()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-CommandAvailability { param([string] $Name) return [bool](Get-Command $Name -ErrorAction SilentlyContinue) }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, static diagnostic => diagnostic.FeatureId == "command.get-command");
        Assert.DoesNotContain("__invokePowerShellCapture", typed.SourceCode, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", typed.SourceCode, StringComparison.Ordinal);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeFreeCommandDiscovery",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactPath);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Contains("No PowerShell functions were eligible", result.Error, StringComparison.Ordinal);
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            capabilities: PowerShellCompilationCapability.None));
        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == "command.get-command");
    }

    [Fact]
    public void Build_StrictLibraryAbiMarksEnvironmentValueAsNullable()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MissingEnvironmentValue { return $env:POWERFORGE_ENVIRONMENT_VALUE_THAT_DOES_NOT_EXIST }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableEnvironmentAbi",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var method = Assert.Single(result.Manifest!.PublicAbi!.Methods);
        Assert.Contains("Unknown", method.OutputValueStates);
        Assert.True(method.CanProduceNull);
        Assert.True(method.Nullable);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModuleSnapshotsSupportedPreferencesAndErrorCollection(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-BoundedRuntimeState { [CmdletBinding(SupportsShouldProcess = $true)] param() " +
            "return @($VerbosePreference.ToString(), $DebugPreference.ToString(), $WarningPreference.ToString(), $InformationPreference.ToString(), $ErrorActionPreference.ToString(), $ProgressPreference.ToString(), $ConfirmPreference.ToString(), $Error.Count) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BoundedRuntimeStateModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var setup = "$VerbosePreference='SilentlyContinue'; $DebugPreference='SilentlyContinue'; $WarningPreference='Continue'; $InformationPreference='SilentlyContinue'; $ErrorActionPreference='Continue'; $ProgressPreference='Continue'; $ConfirmPreference='High'; $global:Error.Clear();";
        var invocation = targetFramework == "net472"
            ? "Get-BoundedRuntimeState -Verbose -Debug -WarningAction Stop -InformationAction Ignore -ErrorAction Stop -Confirm:$false"
            : "Get-BoundedRuntimeState -Verbose -Debug -WarningAction Stop -InformationAction Ignore -ErrorAction Stop -ProgressAction Ignore -Confirm:$false";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {setup} {invocation}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {setup} {invocation}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesEditionAndVersionState(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-EditionState { return $PSEdition }; " +
            "function Get-VersionState { return $PSVersionTable.PSVersion.ToString() }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        const string calls = "Get-EditionState; Get-VersionState";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesProcessUserAndCultureState(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-ProcessUserCultureState { return @(($PID -gt 0), $HOME, $PSCulture, $PSUICulture) }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.ProcessUserCultureState",
            "CompiledPowerShell",
            targetFramework);

        var method = Assert.Single(typed.Methods);
        Assert.False(method.RequiresPowerShellRuntimeState);
        Assert.Empty(typed.Diagnostics);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ProcessUserCultureState",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string command = "Get-ProcessUserCultureState";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Transpile_BinaryModuleBindsOnlyExactExecutionContextLanguageMode()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LanguageMode { return $ExecutionContext.SessionState.LanguageMode } " +
            "function Get-LanguageModeText { return [string] $ExecutionContext.SessionState.LanguageMode }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.LanguageModeState",
            "CompiledPowerShell",
            "net10.0");

        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        Assert.Equal(2, typed.Methods.Length);
        Assert.All(typed.Methods, static method => Assert.True(method.RequiresPowerShellRuntimeState));
        Assert.Contains(
            "(global::System.Management.Automation.PSLanguageMode)__runtimeState[\"LanguageMode\"]!",
            typed.SourceCode,
            StringComparison.Ordinal);
        var wrapper = PowerShellBinaryCmdletSourceGenerator.Generate(typed, targetFramework: "net10.0");
        Assert.Contains("values[\"LanguageMode\"] = SessionState.LanguageMode", wrapper, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return $ExecutionContext.SessionState")]
    [InlineData("return $ExecutionContext.SessionState.Applications")]
    [InlineData("return $ExecutionContext.InvokeCommand")]
    [InlineData("param([object] $ExecutionContext) return $ExecutionContext.SessionState.LanguageMode")]
    [InlineData("$ExecutionContext.SessionState.LanguageMode = [System.Management.Automation.PSLanguageMode]::RestrictedLanguage; return $true")]
    public void Analyze_ExecutionContextStateRemainsBounded(string body)
    {
        using var fixture = ArtifactFixture.Create($"function Get-RuntimeState {{ {body} }}", ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapabilities.BinaryModule)).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.NotEmpty(unit.Diagnostics);
    }

    [Fact]
    public void Analyze_RuntimeFreeLibraryRejectsExecutionContextLanguageMode()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LanguageMode { return $ExecutionContext.SessionState.LanguageMode }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationSpec(
                fixture.ScriptPath,
                PowerShellCompilationMode.Strict,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapabilities.StaticRuntimeFacts)).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesExecutionContextLanguageMode(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-LanguageMode { return $ExecutionContext.SessionState.LanguageMode }; " +
            "function Get-LanguageModeText { return [string] $ExecutionContext.SessionState.LanguageMode }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LanguageModeState",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.RequiresPowerShellRuntime);
        const string calls = "Get-LanguageMode; Get-LanguageModeText";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesShouldProcessAndWhatIfState(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Test-RuntimeApproval { [CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) return $PSCmdlet.ShouldProcess($Target, 'Change') }; " +
            "function Set-RuntimeState { [CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) " +
            "if ($WhatIfPreference) { return 'whatif' }; if ($PSCmdlet.ShouldProcess($Target, 'Change')) { return 'changed' }; return 'skipped' }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.RuntimeStateModule",
            "CompiledPowerShell",
            targetFramework);
        Assert.Equal(2, typed.Methods.Length);
        Assert.All(typed.Methods, static method => Assert.True(method.RequiresPowerShellRuntimeState));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        foreach (var command in new[]
                 {
                     "Set-RuntimeState -Target 'item' -Confirm:$false",
                     "Set-RuntimeState -Target 'item' -WhatIf",
                     "$global:WhatIfPreference = $true; Set-RuntimeState -Target 'item' -WhatIf:$false"
                 })
        {
            var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
                $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
            var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
                $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");

            Assert.Equal(0, original.ExitCode);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError + Environment.NewLine + compiled.StandardOutput);
            Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
            Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        }
    }

    [Fact]
    public void Build_InterpolatedRuntimeStatePropagatesThroughLocalCalls()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PreferenceText { [CmdletBinding(SupportsShouldProcess = $true)] param() return \"whatif=$WhatIfPreference\" }; " +
            "function Invoke-PreferenceText { [CmdletBinding(SupportsShouldProcess = $true)] param() return Get-PreferenceText }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.InterpolatedRuntimeState", "CompiledPowerShell", "net8.0");

        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        Assert.Equal(2, typed.Methods.Length);
        Assert.All(typed.Methods, static method => Assert.True(method.RequiresPowerShellRuntimeState));
        Assert.Contains("__whatIfPreference", typed.SourceCode, StringComparison.Ordinal);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InterpolatedRuntimeState",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; " +
            "Get-PreferenceText -WhatIf; Invoke-PreferenceText -WhatIf");
        Assert.Equal(
            new[] { "whatif=True", "whatif=True" },
            compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal((0, string.Empty), (compiled.ExitCode, compiled.StandardError.Trim()));
    }

    [Theory]
    [InlineData("return ([string] $WhatIfPreference) -split 'r'")]
    [InlineData("return ([string[]] @([string] $WhatIfPreference)) -join ','")]
    public void Transpile_StringOperatorsCarryNestedRuntimeStateRequirement(string body)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-PreferenceText {{ [CmdletBinding(SupportsShouldProcess = $true)] param() {body} }}",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.StringRuntimeState", "CompiledPowerShell", "net8.0");

        Assert.Empty(typed.Diagnostics.Select(static diagnostic => diagnostic.Message));
        Assert.True(Assert.Single(typed.Methods).RequiresPowerShellRuntimeState);
        Assert.Contains("__whatIfPreference", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_StrictExecutableKeepsPSCmdletInteractionOnFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(SupportsShouldProcess = $true)] param([string] $Target) return $PSCmdlet.ShouldProcess($Target)");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));

        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
        Assert.Contains(Assert.Single(plan.Files).Units.SelectMany(static unit => unit.Diagnostics), static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Theory]
    [InlineData("return $PSVersionTable.GitCommitId")]
    [InlineData("return $PSCmdlet.ShouldContinue('Continue?', 'Caption')")]
    public void Analyze_RuntimeStateIntrinsicsRemainBounded(string body)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-RuntimeState {{ [CmdletBinding(SupportsShouldProcess = $true)] param() {body} }}",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units, static unit => unit.Kind == PowerShellCompilationUnitKind.Function);

        Assert.False(function.IsCompilable);
        Assert.Contains(function.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.RuntimeScope);
    }

    [Theory]
    [InlineData("return $PSVersionTable.PSVersion::Major")]
    [InlineData("return $PSVersionTable::PSVersion.Major")]
    public void Analyze_StaticVersionMemberSyntaxDoesNotBecomeRuntimeFreeProfileState(string body)
    {
        using var fixture = ArtifactFixture.Create($"function Get-RuntimeState {{ {body} }}", ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var function = Assert.Single(Assert.Single(plan.Files).Units, static unit => unit.Kind == PowerShellCompilationUnitKind.Function);

        Assert.False(function.IsCompilable);
        Assert.NotEmpty(function.Diagnostics);
    }

    [Fact]
    public void Transpile_TypedLibraryLowersStaticRuntimeFacts()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-StaticFact { if ($IsWindows) { return $PSEdition + ':Windows' }; return $PSEdition + ':Other' }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().Transpile(
            fixture.ScriptPath,
            "PowerForge.StaticFacts",
            "CompiledPowerShell",
            "net8.0");

        Assert.True(typed.Methods.Length == 1, string.Join(Environment.NewLine, typed.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.False(typed.Methods[0].RequiresPowerShellRuntimeState);
        Assert.Contains("Core", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform", typed.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LocalWhatIfPreferenceAssignmentIsNotReplacedByHostState()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LocalPreference { [CmdletBinding(SupportsShouldProcess = $true)] param() " +
            "$WhatIfPreference = $false; return $WhatIfPreference }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.LocalWhatIf", "CompiledPowerShell", "net8.0");
        var method = Assert.Single(typed.Methods);
        Assert.False(method.RequiresPowerShellRuntimeState);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LocalWhatIf",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Get-LocalPreference -WhatIf");
        Assert.Equal((0, "False", string.Empty), (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

}
