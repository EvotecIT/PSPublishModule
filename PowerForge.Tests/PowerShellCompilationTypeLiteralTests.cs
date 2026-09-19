namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void TypeLiterals_PreserveValuesInRuntimeFreeAndHostedSurfaces(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-AccountType { $type = [System.Security.Principal.NTAccount]; return $type.FullName }
            function Get-IdentifierType { $type = [System.Security.Principal.SecurityIdentifier]; return $type.FullName }
            function Get-RightsType { $type = [System.Security.AccessControl.FileSystemRights]; return $type.FullName }
            """, ".psm1");

        var library = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.RootPath, "library"), "Generated.TypeLiterals.Library",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(library.Succeeded, library.Error + Environment.NewLine + library.BuildOutput);
        Assert.True(library.Manifest!.CompiledMethods == 3,
            System.Text.Json.JsonSerializer.Serialize(library.Manifest.UnitDispositionLedger));
        Assert.False(library.Manifest.RequiresPowerShellRuntime);

        var module = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.RootPath, "module"), "Generated.TypeLiterals.Module",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(module.Succeeded, module.Error + Environment.NewLine + module.BuildOutput);
        Assert.True(module.Manifest!.CompiledMethods == 3,
            System.Text.Json.JsonSerializer.Serialize(module.Manifest.UnitDispositionLedger));
        Assert.False(module.Manifest.UsesPowerShellRuntimeFallback);

        const string sourceProbe = "Get-AccountType; Get-IdentifierType; Get-RightsType";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + sourceProbe);
        var compiledModule = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + module.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + sourceProbe);
        var compiledLibrary = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "$assembly=[Reflection.Assembly]::LoadFrom('" + library.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'); " +
            "foreach($name in 'Get_AccountType','Get_IdentifierType','Get_RightsType') { " +
            "$method=$assembly.GetTypes().GetMethods() | Where-Object Name -eq $name; $method.Invoke($null,@()) }");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiledModule.ExitCode, compiledModule.StandardOutput.Trim(), compiledModule.StandardError.Trim()));
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiledLibrary.ExitCode, compiledLibrary.StandardOutput.Trim(), compiledLibrary.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedSidConversionPreservesObservedInvocationWrapper(
        string framework,
        string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-SID.ps1");
        Assert.Equal(
            "6f7823741365d6f40b3e3536e020b22936f36245a9d9ebc9849c31c448bf352e",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(source) + Environment.NewLine + "Export-ModuleMember -Function ConvertTo-SID" + Environment.NewLine,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedSidConversion",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "ConvertTo-SID");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);

        const string probe = """
            $first = @(ConvertTo-SID -Identity '__PowerForge_Missing_Identity_A__','__PowerForge_Missing_Identity_B__')
            $recent = $Error[0]
            'error-type=' + $recent.Exception.GetType().FullName
            'inner-type=' + $(if ($recent.Exception.InnerException) { $recent.Exception.InnerException.GetType().FullName } else { '' })
            'error-id=' + $recent.FullyQualifiedErrorId
            $first | Select-Object Name,Sid,Error | ConvertTo-Json -Compress
            $again = @(ConvertTo-SID -Identity '__PowerForge_Missing_Identity_A__')
            'repeat=' + ($again[0].Name + ':' + $again[0].Sid + ':' + $again[0].Error)
            Remove-Module $module.Name
            $module = Import-Module $modulePath -PassThru
            $reimport = @(ConvertTo-SID -Identity '__PowerForge_Missing_Identity_A__')
            'reimport=' + ($reimport[0].Name + ':' + $reimport[0].Sid + ':' + $reimport[0].Error)
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "pinned-sid-conversion-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "pinned-sid-conversion-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original: " + original.StandardOutput + Environment.NewLine + "Generated: " + compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedIdentityConversionPreservesCaughtRecords(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Converts", "Convert-Identity.ps1");
        Assert.Equal(
            "f9fc533d2291fc7458ac77e5b821a9ecdc3f868fd1e4bee184cc3937653bd2c6",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(source) + Environment.NewLine + "Export-ModuleMember -Function Convert-Identity" + Environment.NewLine,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedIdentityConversion",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "Convert-Identity");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);

        const string probe = """
            $first = @(Convert-Identity -Identity '__PowerForge_Missing_Identity_C__','__PowerForge_Missing_Identity_D__')
            $first | Select-Object Name,SID,Error,Type | ConvertTo-Json -Compress
            $again = @(Convert-Identity -Identity '__PowerForge_Missing_Identity_C__')
            'repeat=' + ($again[0].Name + ':' + $again[0].SID + ':' + $again[0].Error)
            Remove-Module $module.Name
            $module = Import-Module $modulePath -PassThru
            $reimport = @(Convert-Identity -Identity '__PowerForge_Missing_Identity_C__')
            'reimport=' + ($reimport[0].Name + ':' + $reimport[0].SID + ':' + $reimport[0].Error)
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "pinned-identity-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "pinned-identity-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ObservedInvocation_DirectTypeLiteralCatchAllPreservesCaughtRecordIdentity(
        string framework,
        string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-CaughtTranslationError {
                [CmdletBinding()] param([string]$Value)
                process {
                    try { ([System.Security.Principal.NTAccount]$Value).Translate([System.Security.Principal.SecurityIdentifier]) }
                    catch {
                        $caughtException = $_.Exception
                        'exception-type=' + $caughtException.GetType().FullName
                        'inner-type=' + $caughtException.InnerException.GetType().FullName
                        'error-id=' + $_.FullyQualifiedErrorId
                        'category=' + $_.CategoryInfo.Category
                    }
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CaughtTypeLiteralRecord",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);

        const string probe =
            "Get-CaughtTranslationError -Value '__PowerForge_Missing_Caught_Identity__'";
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "caught-record-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "caught-record-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True((original.ExitCode, original.StandardOutput, original.StandardError) ==
            (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError),
            "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Generated: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Fact]
    public void TypeLiteral_UnresolvedTypeFailsClosed()
    {
        using var fixture = ArtifactFixture.Create("function Get-MissingType { [Missing.PowerForge.Type] }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.MissingType",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.False(result.Succeeded);
        Assert.Contains("not statically available", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ObservedInvocation_DirectTypeLiteralWithTypedCatchPreservesSelection(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Convert-TypedIdentity {
                [CmdletBinding()] param([string]$Value)
                try { ([System.Security.Principal.NTAccount]$Value).Translate([System.Security.Principal.SecurityIdentifier]) }
                catch [System.Security.Principal.IdentityNotMappedException] { 'typed' }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedCatchTypeLiteral",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod);
        const string probe = "Convert-TypedIdentity -Value '__PowerForge_Missing_Typed_Identity__'";
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-type-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-type-compiled");
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }

    [Fact]
    public void TypeLiteral_RejectsTypeMissingFromTargetFramework()
    {
        using var fixture = ArtifactFixture.Create("function Get-ModernType { [System.DateOnly] }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ModernTypeForLegacyTarget",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net472" });
        Assert.False(result.Succeeded);
        Assert.Contains("not statically available", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
