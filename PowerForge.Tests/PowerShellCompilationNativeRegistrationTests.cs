using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeRegistration_PreservesDeclarationPreferencesAliasesAndStatus(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            $ConfirmPreference = 'Low'
            $WhatIfPreference = $env:REGION_REGISTRATION_MODE -eq 'WhatIf'
            $PSDefaultParameterValues = @{}
            if ($env:REGION_REGISTRATION_MODE -eq 'Defaults') {
                $PSDefaultParameterValues['Set-Item:WhatIf'] = $true
                $PSDefaultParameterValues['Set-Alias:WhatIf'] = $true
            }
            if ($env:REGION_REGISTRATION_MODE -eq 'PassThru') {
                $PSDefaultParameterValues['Set-Item:PassThru'] = $true
                $PSDefaultParameterValues['Set-Alias:PassThru'] = $true
            }
            Write-Error 'before guarded declaration' -ErrorAction SilentlyContinue
            function Get-GuardedRegistration {
                [Alias('GuardedRegistration')]
                param([bool] $Enabled)
                $number = 1
                if ($Enabled) { $number = 2 }
                data RegistrationBarrier { 'retained' }
                & { $number }
            }
            $script:GuardedDeclarationStatus = $?
            Write-Error 'before native declaration' -ErrorAction SilentlyContinue
            function Get-NativeRegistration {
                [Alias('NativeRegistration')]
                [CmdletBinding()]
                param([ValidateRange(0, 10)][int] $Number)
                return $Number
            }
            $script:NativeDeclarationStatus = $?
            function <# preserve the name token #> Get-Escaped` Native([ValidateRange(0, 10)][int] $Number) { return $Number }
            function Get-RegistrationStatus { & { $script:GuardedDeclarationStatus; $script:NativeDeclarationStatus } }
            Export-ModuleMember -Function Get-GuardedRegistration, Get-NativeRegistration, Get-RegistrationStatus, 'Get-Escaped Native' -Alias GuardedRegistration, NativeRegistration
            """, ".psm1");
        var plan = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.Registration", "Methods", framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.True(Assert.Single(plan.PromotedRegions, region => region.SourceName == "Get-GuardedRegistration").RequiresLocalOwnershipGuard);
        Assert.NotNull(Assert.Single(plan.Methods, method => method.SourceName == "Get-NativeRegistration").NativeFunctionBinding);
        Assert.NotNull(Assert.Single(plan.Methods, method => method.SourceName == "Get-Escaped Native").NativeFunctionBinding);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeRegistration", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = """
            $ErrorActionPreference = 'Stop'
            $observed = @(foreach ($mode in 'WhatIf', 'Defaults', 'PassThru') {
                $env:REGION_REGISTRATION_MODE = $mode
                $Error.Clear()
                $imported = @(Import-Module $modulePath -PassThru -Force)
                [pscustomobject]@{
                    Mode = $mode
                    ImportCount = $imported.Count
                    Values = @(Get-GuardedRegistration $true; GuardedRegistration $false; Get-NativeRegistration 7; NativeRegistration 8; & 'Get-Escaped Native' 9)
                    Status = @(Get-RegistrationStatus)
                    Errors = @($Error | ForEach-Object { $_.FullyQualifiedErrorId.Replace(',' + [IO.Path]::GetFileName($modulePath), ',<module>') })
                    Aliases = @((Get-Alias GuardedRegistration).Definition; (Get-Alias NativeRegistration).Definition)
                }
            })
            ConvertTo-Json -InputObject $observed -Depth 6 -Compress
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-registration");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-registration");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var observed = System.Text.Json.Nodes.JsonNode.Parse(original.StandardOutput)!.AsArray();
        Assert.Equal(3, observed.Count);
        Assert.All(observed, observation =>
        {
            Assert.Equal(1, observation!["ImportCount"]!.GetValue<int>());
            Assert.Equal(new[] { 2, 1, 7, 8, 9 }, observation["Values"]!.AsArray().Select(value => value!.GetValue<int>()));
            Assert.Equal(new[] { false, false }, observation["Status"]!.AsArray().Select(value => value!.GetValue<bool>()));
        });
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original: " + original.StandardOutput + " Artifact: " + compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeRegistrationFailureHosts))]
    public void NativeRegistration_PreservesReadOnlyDeclarationFailures(string framework, string host, bool wholeFunction)
    {
        const string source = """
            $ErrorActionPreference = $env:REGION_REGISTRATION_ACTION
            $option = if ($env:REGION_REGISTRATION_MODE -like 'Constant*') { 'Constant' } else { 'ReadOnly' }
            if ($env:REGION_REGISTRATION_MODE -like '*Function') {
                Set-Item -LiteralPath Function:\script:Get-ProtectedRegistration -Value { 99 } -Options $option
            } else {
                Set-Alias -Name ProtectedRegistration -Value Get-Date -Scope Script -Option $option
            }
            $Error.Clear()
            function Get-ProtectedRegistration {
                [Alias('ProtectedRegistration')]
                param([bool] $Enabled)
                $number = 1
                if ($Enabled) { $number = 2 }
                data RegistrationBarrier { 'retained' }
                & { $number }
            }
            $script:DeclarationStatus = $?
            $script:DeclarationErrors = @($Error | ForEach-Object {
                [pscustomobject]@{ Id = $_.FullyQualifiedErrorId; Type = $_.Exception.GetType().FullName; Category = $_.CategoryInfo.Category.ToString() }
            })
            Export-ModuleMember -Function Get-ProtectedRegistration -Alias *
            """;
        using var fixture = ArtifactFixture.Create(wholeFunction
            ? source.Replace("param([bool] $Enabled)", "param([ValidateRange(0, 1)][bool] $Enabled)")
                .Replace("data RegistrationBarrier { 'retained' }", string.Empty).Replace("& { $number }", "return $number")
            : source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeRegistrationFailures", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        if (wholeFunction) Assert.Equal(1, result.Manifest!.CompiledMethods);
        else Assert.True(result.Manifest!.PromotedTypedRegions > 0);
        const string probe = """
            $observed = @(foreach ($mode in 'Function', 'Alias', 'ConstantFunction', 'ConstantAlias') {
                foreach ($action in 'Continue', 'SilentlyContinue', 'Ignore', 'Stop') {
                    & {
                        param($mode, $action)
                        # A constant export must expire with its caller scope before the next import.
                        $env:REGION_REGISTRATION_MODE = $mode
                        $env:REGION_REGISTRATION_ACTION = $action
                        $module = $null
                        $caught = $null
                        $Error.Clear()
                        if ($action -eq 'Stop') {
                            try { $module = Import-Module $modulePath -PassThru -Force -Scope Local 2>$null }
                            catch { $caught = $_.Exception.GetType().FullName }
                        } else {
                            $module = Import-Module $modulePath -PassThru -Force -Scope Local 2>$null
                        }
                        $errors = @($Error | ForEach-Object {
                            [pscustomobject]@{ Id = $_.FullyQualifiedErrorId; Type = $_.Exception.GetType().FullName; Category = $_.CategoryInfo.Category.ToString() }
                        })
                        $state = if ($module) { & $module {
                            [pscustomobject]@{
                                Status = $script:DeclarationStatus
                                Errors = $script:DeclarationErrors
                                Value = @(Get-ProtectedRegistration $true)
                                Alias = $(if (Test-Path Alias:\ProtectedRegistration) { (Get-Alias ProtectedRegistration).Definition })
                            }
                        } }
                        [pscustomobject]@{ Mode = $mode; Action = $action; Caught = $caught; Errors = $errors; State = $state }
                    } $mode $action
                }
            })
            ConvertTo-Json -InputObject $observed -Depth 6 -Compress
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-registration-failures");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-registration-failures");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var observed = System.Text.Json.Nodes.JsonNode.Parse(original.StandardOutput)!.AsArray();
        Assert.Equal(16, observed.Count);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original: " + original.StandardOutput + " Artifact: " + compiled.StandardOutput);
        Assert.Equal(99, observed[0]!["State"]!["Value"]![0]!.GetValue<int>());
        Assert.Equal(2, observed[4]!["State"]!["Value"]![0]!.GetValue<int>());
        Assert.Equal("FunctionNotWritable", observed[0]!["Errors"]![0]!["Id"]!.GetValue<string>());
        Assert.Equal("AliasNotWritable", observed[4]!["Errors"]![0]!["Id"]!.GetValue<string>());
        Assert.Equal(99, observed[8]!["State"]!["Value"]![0]!.GetValue<int>());
        Assert.True(observed[12]?["State"]?["Value"]?[0]?.GetValue<int>() == 2, original.StandardOutput);
        Assert.All(new[] { observed[0], observed[4], observed[8], observed[12] }, observation =>
        {
            Assert.False(observation!["State"]!["Status"]!.GetValue<bool>());
            Assert.Single(observation["Errors"]!.AsArray());
        });
    }

    public static IEnumerable<object[]> NativeRegistrationFailureHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { false, true }
            .Select(wholeFunction => new[] { configuration[0], configuration[1], (object)wholeFunction }));
}
