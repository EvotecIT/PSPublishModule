using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HybridGuardedRegions_PreserveAuthoredFunctionScopes(string framework, string host)
    {
        var source = string.Join(Environment.NewLine, new[] { "local", "script", "private", "global" }.Select(scope =>
            "function " + scope + ":Get-RegionScope" + scope + " { [Alias('RegionAlias" + scope + "')] param([bool] $Enabled); $number = 1; if ($Enabled) { $number = 2 }; data ScopeBarrier { 'retained' }; & { $number } }" + Environment.NewLine +
            "function " + scope + ":Get-NativeScope" + scope + " { [Alias('NativeAlias" + scope + "')] param([ValidateRange(0, 10)][int] $Number); return $Number }")) +
            Environment.NewLine + "$script:DeclarationScopeValues = @(Get-RegionScopeprivate $true; Get-NativeScopeprivate 7)";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RegionFunctionScopes", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.PromotedTypedRegions);
        Assert.Equal(4, result.Manifest.CompiledMethods);
        const string probe = "& $module { $script:DeclarationScopeValues; foreach($scope in 'local','script','private','global') { $name='Get-RegionScope'+$scope; try { & $name $true } catch { 'error:'+ $_.FullyQualifiedErrorId }; $command=Get-Command $name -ErrorAction SilentlyContinue; if($command) { $command.Options.ToString() } else { 'missing' }; $alias='RegionAlias'+$scope; try { & $alias $true } catch { 'alias-error:'+ $_.FullyQualifiedErrorId }; (Get-Alias $alias).Definition; $name='Get-NativeScope'+$scope; try { & $name 7 } catch { 'native-error:'+ $_.FullyQualifiedErrorId }; $alias='NativeAlias'+$scope; try { & $alias 8 } catch { 'native-alias-error:'+ $_.FullyQualifiedErrorId }; (Get-Alias $alias).Definition } }";
        var original = RunStatementErrorProbe(host, "$module = Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "' -PassThru; " + probe,
            fixture.RootPath, "original-region-scopes");
        var compiled = RunStatementErrorProbe(host, "$module = Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "' -PassThru; " + probe,
            fixture.RootPath, "compiled-region-scopes");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.StartsWith("2" + Environment.NewLine + "7" + Environment.NewLine, original.StandardOutput);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original: " + original.StandardOutput + " Artifact: " + compiled.StandardOutput);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError), (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HybridRegions_PreserveHostedClassIdentityWithoutDetachingItsFunction(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            using namespace System.Text
            class RegionSourceValue { [string] $Text = 'class-value' }
            function Get-HostedClass([bool] $Enabled) {
                $number = 1
                if ($Enabled) { $number = 2 }
                data SourceBarrier { 'retained' }
                & { ([StringBuilder]::new().Append($number)).ToString(); [RegionSourceValue]::new() }
            }
            function Test-HostedClass($Value) { $Value -is [RegionSourceValue] }
            Export-ModuleMember -Function Get-HostedClass, Test-HostedClass
            """, ".psm1");
        var plan = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.RegionClass", "Methods", framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(plan.PromotedRegions, region => region.RequiresLocalOwnershipGuard);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Message.Contains("runtime type identities", StringComparison.Ordinal));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RegionClassIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = "$values = @(Get-HostedClass $true); $values[0]; $values[1].Text; Test-HostedClass $values[1]";
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-region-class");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-region-class");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.Equal("2" + Environment.NewLine + "class-value" + Environment.NewLine + "True", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError), (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HybridGuardedRegion_PreservesUsingTypesInlineParametersAndMixedHostNeighbors(string framework, string host)
    {
        const string cleanNeighbor = "function Invoke-NewerNeighbor { [CmdletBinding()] param([object] $Trace) end { $Trace.Add('end'); 'end' } clean { $Trace.Add('clean'); 'hidden' } }";
        const string source = """
            using namespace System.Text
            function Get-SourceContext([bool] $Enabled) {
                $number = 1
                if ($Enabled) { $number = 2 }
                data SourceBarrier { 'retained' }
                & { ([StringBuilder]::new().Append($number)).ToString() }
            }
            CLEAN_NEIGHBOR
            Export-ModuleMember -Function Get-SourceContext, Invoke-NewerNeighbor
            """;
        using var fixture = ArtifactFixture.Create(source.Replace("CLEAN_NEIGHBOR", cleanNeighbor, StringComparison.Ordinal), ".psm1");
        var plan = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.RegionContext", "Methods", framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.True(Assert.Single(plan.PromotedRegions, region => region.SourceName == "Get-SourceContext").RequiresLocalOwnershipGuard);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RegionSourceContext", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        // The older oracle cannot parse the unrelated clean clause. Preserve the selected source
        // function verbatim; the artifact must import its full mixed-host selection on every host.
        if (framework == "net472")
            File.WriteAllText(fixture.ScriptPath, source.Replace("CLEAN_NEIGHBOR", new string(' ', cleanNeighbor.Length), StringComparison.Ordinal));
        const string probe = "Get-SourceContext $true; Get-SourceContext $false; (Get-Command Get-SourceContext).Parameters['Enabled'].ParameterType.FullName";
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-region-context");
        var compiled = RunStatementErrorProbe(host, "$Error.Clear(); Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; if($Error.Count) { throw $Error[0].Exception.ToString() }; " + probe,
            fixture.RootPath, "compiled-region-context");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.Contains("2" + Environment.NewLine + "1" + Environment.NewLine + "System.Boolean", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.StandardOutput, original.StandardError), (compiled.StandardOutput, compiled.StandardError));
    }
}
