using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void PinnedDateTimeStringArgument_PreservesOriginalConversionAndCatch(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        var sourcePath = Path.Combine(FindStaticNumericRepositoryRoot(), "Benchmarks", "PowerShellCompilation",
            "Corpus", "ExternalWorkflows", "PSSharedGoods", "FullModule", "Public", "Converts", "Convert-ToDateTime.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(sourcePath), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.DateTimeStringArgument",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "Convert-ToDateTime");
        Assert.True(unit.EmittedClrMethod);
        Assert.True(unit.UsesNativeFunctionBinding);

        const string probe = """
            foreach ($sample in @('132479040000000000','0','abc','999999999999999999999999999','')) {
                $Error.Clear()
                try {
                    $value = Convert-ToDateTime -Timestring $sample -ErrorAction Stop
                    [pscustomobject]@{input=$sample;type=if($null -eq $value){'null'}else{$value.GetType().FullName};value=if($null -eq $value){$null}else{$value.ToString('O')};errors=@($Error).Count} | ConvertTo-Json -Compress
                } catch { [pscustomobject]@{input=$sample;caught=$_.FullyQualifiedErrorId;errors=@($Error).Count} | ConvertTo-Json -Compress }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(5, generated.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void StringToNumericInvocation_LeavesOverloadedParseOnTypedPathAndStrictHosted()
    {
        const string source = """
            function Get-FileTime { [CmdletBinding()] param([string] $Text) return [datetime]::FromFileTime($Text) }
            function Get-ParsedNumber { [CmdletBinding()] param([string] $Text) return [long]::Parse($Text) }
            """;
        var document = PowerShellSourceParser.Parse(source,
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "static-numeric-invocation.psm1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(hybrid.Emitted.Methods, static method => method.GeneratedName == "Get_FileTime" &&
            method.NativeFunctionBinding is not null);
        Assert.Contains(hybrid.Emitted.Methods, static method => method.GeneratedName == "Get_ParsedNumber" &&
            method.NativeFunctionBinding is null);

        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.TypedLibrary);
        Assert.DoesNotContain(strict.Emitted.Methods, static method => method.GeneratedName == "Get_FileTime");
        Assert.Contains(strict.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSB2609");
    }

    private static string FindStaticNumericRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln")))
                return directory.FullName;
        throw new InvalidOperationException("Unable to locate the compiler repository root.");
    }
}
