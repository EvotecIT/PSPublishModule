using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeIndexedMapWorkflow_PreservesLocalValuesModuleCacheAndIdentity(string framework, string host)
    {
        var sources = new[]
        {
            "Convert-CountryCodeToCountry.ps1",
            "Convert-ExchangeRecipient.ps1",
            "ConvertFrom-OperationType.ps1"
        }.Select(name => File.ReadAllText(FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Converts", name)))
            .Concat(new[]
            {
                "function Read-ClosedIndex { [CmdletBinding()] param([string[]]$Values, [int]$Index) return $Values[$Index] }",
                "function Read-ClosedMap { [CmdletBinding()] param([string]$Key) $m=@{a='A';b='B'}; return $m[$Key] }"
            });
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.IndexedMapWorkflow",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(5, result.Manifest!.CompiledMethods);
        var units = result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function).ToArray();
        Assert.All(units.Where(unit => unit.Name is not ("Read-ClosedIndex" or "Read-ClosedMap")),
            unit =>
            {
                Assert.True(unit.EmittedClrMethod);
                Assert.True(unit.UsesNativeFunctionBinding);
                Assert.False(unit.RetainedHostedSource);
            });
        foreach (var name in new[] { "Read-ClosedIndex", "Read-ClosedMap" })
        {
            var closed = Assert.Single(units, unit => unit.Name == name);
            Assert.True(closed.EmittedClrMethod);
            Assert.False(closed.UsesNativeFunctionBinding);
        }

        const string probe = """
            $first=Convert-CountryCodeToCountry -CountryCode PL
            $map=Convert-CountryCodeToCountry
            $details=Convert-CountryCodeToCountry -CountryCode PL -All
            $same=[object]::ReferenceEquals($details,(Convert-CountryCodeToCountry -CountryCode PL -All))
            $missing=@(Convert-CountryCodeToCountry -CountryCode ZZ)
            $recipient=Convert-ExchangeRecipient -msExchRecipientTypeDetails 4
            $unknown=Convert-ExchangeRecipient -msExchRecipientTypeDetails 999999
            $all=Convert-ExchangeRecipient -msExchRecipientTypeDetails 4 -All
            $operation=ConvertFrom-OperationType -OperationType '%%14674'
            $fallback=ConvertFrom-OperationType -OperationType '%%99999'
            [pscustomobject]@{
                country=$first;mapType=$map.GetType().FullName;mapCount=$map.Count;
                mapPoland=$map['PL'].RegionInformation.EnglishName;detailsType=$details.GetType().FullName;
                detailsName=$details.RegionInformation.EnglishName;sameReference=$same;
                missingCount=$missing.Count;missingIsNull=($missing.Count -eq 1 -and $null -eq $missing[0]);
                recipient=$recipient;unknown=$unknown;allType=$all.GetType().FullName;
                allCount=$all.Count;allFour=$all['4'];operation=$operation;fallback=$fallback;
                closed=(Read-ClosedIndex -Values @('first','second') -Index 1);
                closedMap=(Read-ClosedMap -Key b)
            } | ConvertTo-Json -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-indexed-map");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-indexed-map");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
