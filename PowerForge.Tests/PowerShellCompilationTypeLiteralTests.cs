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
    public void CompleteWorkflow_PinnedSidConversionRetainsObservedInvocationWrapper(
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
        Assert.False(unit.EmittedClrMethod);
        Assert.False(unit.UsesNativeFunctionBinding);
        Assert.True(unit.RetainedHostedSource);
        Assert.Contains(unit.DiagnosticChain, static diagnostic =>
            diagnostic.Message.Contains("method-invocation wrapper identity", StringComparison.Ordinal));

        const string probe = """
            $first = @(ConvertTo-SID -Identity '__PowerForge_Missing_Identity_A__','__PowerForge_Missing_Identity_B__')
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
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
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
