namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void RuntimeTargetConversion_PreservesHostResolutionConversionAndReuse(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Convert-ToDirectoryEntry {
                [CmdletBinding()] param([object]$Value)
                [ADSI]$Value
            }
            """, ".psm1");

        var strict = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.RootPath, "strict"), "Generated.RuntimeTarget.Strict",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.False(strict.Succeeded);
        Assert.Contains("not available in the generated target contract", strict.Error, StringComparison.OrdinalIgnoreCase);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RuntimeTarget.Module",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "Convert-ToDirectoryEntry");
        Assert.True(unit.EmittedClrMethod);
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);

        const string probe = """
            function Describe-Record($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{error=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        category=[string]$item.CategoryInfo.Category;message=$item.Exception.Message}
                } elseif ($null -eq $item) { [pscustomobject]@{type='null';isDirectoryEntry=$false} }
                else { [pscustomobject]@{type='value';isDirectoryEntry=($item -is [DirectoryServices.DirectoryEntry])} }
            }
            $cases=@(
                @{name='empty';value=''}, @{name='ldap';value='LDAP://example.invalid'},
                @{name='integer';value=123}, @{name='null';value=$null},
                @{name='array';value=@('LDAP://one.invalid','LDAP://two.invalid')})
            foreach($case in $cases) {
                foreach($action in 'Continue','Stop') {
                    $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                    try { Convert-ToDirectoryEntry -Value $case.value -ErrorAction $action -ErrorVariable faults 2>&1 |
                        ForEach-Object { $records.Add((Describe-Record $_)) } }
                    catch { $records.Add((Describe-Record $_)) }
                    [pscustomobject]@{case=$case.name;action=$action;records=$records.ToArray();
                        faults=@($faults | ForEach-Object { Describe-Record $_ });
                        errors=@($Error | ForEach-Object { Describe-Record $_ })} | ConvertTo-Json -Depth 8 -Compress
                }
            }
            Remove-Module $module.Name
            $module=Import-Module $modulePath -PassThru
            'reimport=' + (@(Convert-ToDirectoryEntry -Value '').Count)
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "runtime-target-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "runtime-target-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("Convert-DomainToSid.ps1", "5d51cb22e051605880ed678eeee9b07d81ed40da53def00bb2471259711f5128", "Convert-DomainToSid")]
    [InlineData("Convert-DomainFqdnToNetBIOS.ps1", "3cc96dc766b8a0d82335445ca20190d03b5fd5bbe50a0a96e63c8530915279af", "Convert-DomainFqdnToNetBIOS")]
    [InlineData("ConvertFrom-NetbiosName.ps1", "f65488348776a529068b221200a3419b9aa3a80a28b4b0cbdc312a15852315dd", "ConvertFrom-NetbiosName")]
    public void CompleteWorkflow_RuntimeTargetConversionsEmitUnchangedFunctions(
        string fileName,
        string expectedSha256,
        string functionName)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", fileName);
        Assert.Equal(expectedSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        var sourceText = File.ReadAllText(source);
        if (fileName.Equals("ConvertFrom-NetbiosName.ps1", StringComparison.OrdinalIgnoreCase))
        {
            var dependency = FindCompleteConversionWorkflow(
                "PSSharedGoods", "FullModule", "Public", "Converts", "ConvertFrom-DistinguishedName.ps1");
            sourceText += Environment.NewLine + File.ReadAllText(dependency);
        }
        using var fixture = ArtifactFixture.Create(sourceText, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedRuntimeTarget." + functionName.Replace('-', '_'),
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            entry => entry.Name == functionName);
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ObservedInvocation_TransitivelyHoistedTypeLiteralRemainsHosted(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Convert-HoistedIdentity {
                [CmdletBinding()] param([string]$Value)
                $null = $Value -match '.'
                $targetType = [System.Security.Principal.SecurityIdentifier]
                $alias = $targetType
                $forwarded = $alias
                try { ([System.Security.Principal.NTAccount]$Value).Translate($forwarded) }
                catch { '{0}|{1}|{2}|{3}' -f $_.Exception.GetType().FullName,$_.Exception.Message,$_.FullyQualifiedErrorId,$_.Exception.InnerException.GetType().FullName }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HoistedTypeLiteral",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.False(unit.EmittedClrMethod);
        Assert.True(unit.RetainedHostedSource);
        Assert.Contains(unit.DiagnosticChain, static diagnostic =>
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.ForSyntax("InvokeMemberExpressionAst") &&
            diagnostic.Message.Contains("caught-error identity", StringComparison.Ordinal));
        const string probe = "Convert-HoistedIdentity -Value '__PowerForge_Missing_Hoisted_A__'; Convert-HoistedIdentity -Value '__PowerForge_Missing_Hoisted_B__'";
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "hoisted-type-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "hoisted-type-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal((original.StandardOutput, original.StandardError),
            (compiled.StandardOutput, compiled.StandardError));
    }
}
