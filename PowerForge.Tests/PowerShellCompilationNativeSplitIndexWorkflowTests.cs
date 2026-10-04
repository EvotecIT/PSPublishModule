namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeSplitIndex_PreservesCardinalityPatternErrorsAndContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-FirstSplit {
                [CmdletBinding()] param([object]$Value, [object]$Pattern, [switch]$CaseSensitive)
                if ($CaseSensitive) { return ($Value -csplit $Pattern)[0] }
                return ($Value -split $Pattern)[0]
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeSplitIndex",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);

        const string probe = """
            $cases = @(
                @{value='A|b';pattern='\|'}, @{value='single';pattern='\|'},
                @{value='';pattern='\|'}, @{value=$null;pattern='\|'},
                @{value=@('A|b','C|d');pattern='\|'}, @{value='A|b';pattern='['})
            for ($round = 0; $round -lt 2; $round++) {
                if ($round -gt 0) { Remove-Module $module.Name; $module = Import-Module $modulePath -PassThru }
                foreach ($sensitive in $false,$true) {
                    for ($index = 0; $index -lt $cases.Count; $index++) {
                        $case = $cases[$index]; $Error.Clear(); $records = [Collections.Generic.List[object]]::new()
                        try {
                            Read-FirstSplit -Value $case.value -Pattern $case.pattern -CaseSensitive:$sensitive -ErrorAction Stop 2>&1 |
                                ForEach-Object { $records.Add($_) }
                        } catch { $records.Add($_) }
                        $observed = @($records | ForEach-Object {
                            if ($_ -is [Management.Automation.ErrorRecord]) {
                                [pscustomobject]@{kind='error';type=$_.Exception.GetType().FullName;
                                    id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category}
                            } else { [pscustomobject]@{kind='value';type=$_.GetType().FullName;value=[string]$_} }
                        })
                        [pscustomobject]@{round=$round;sensitive=$sensitive;case=$index;
                            count=$records.Count;records=$observed;errorCount=$Error.Count} |
                            ConvertTo-Json -Depth 6 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "split-index-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "split-index-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(24, original.StandardOutput.Split('\n').Count(static line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeSplitIndex_PreservesPinnedX500Conversion(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods", "FullModule", "Public", "Converts", "ConvertFrom-X500Address.ps1");
        Assert.Equal("da7be07cea958f5cbeabf3685a943c002621dca3f5293c0dfa23c992037857e9",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(source) + Environment.NewLine + "Export-ModuleMember -Function ConvertFrom-X500Address" + Environment.NewLine,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedX500Conversion",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "ConvertFrom-X500Address");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);

        const string probe = """
            $cases = @(
                @{text='IMCEAEX-a_b+20c@domain.example';full=$false},
                @{text='IMCEAEX-a_b+20c@domain.example';full=$true},
                @{text='IMCEAEX-one@two@three';full=$false},
                @{text='IMCEAEX-';full=$false},
                @{text='IMCEAEX-_o=AD_ou=東京+2EOffice+2C+20North@evotec.pl';full=$true},
                @{text=$null;full=$false},
                @{text=$null;full=$true})
            for ($round = 0; $round -lt 2; $round++) {
                if ($round -gt 0) { Remove-Module $module.Name; $module = Import-Module $modulePath -PassThru }
                for ($index = 0; $index -lt $cases.Count; $index++) {
                    $case = $cases[$index]; $Error.Clear()
                    $records = @(ConvertFrom-X500Address -IMCEAEXString $case.text -Full:$case.full 2>&1)
                    $observed = @($records | ForEach-Object {
                        if ($_ -is [Management.Automation.ErrorRecord]) {
                            [pscustomobject]@{kind='error';type=$_.Exception.GetType().FullName;
                                id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category}
                        } else { [pscustomobject]@{kind='value';type=$_.GetType().FullName;value=[string]$_} }
                    })
                    [pscustomobject]@{round=$round;case=$index;count=$records.Count;
                        records=$observed;errorCount=$Error.Count} | ConvertTo-Json -Depth 6 -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "x500-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "x500-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(14, original.StandardOutput.Split('\n').Count(static line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
