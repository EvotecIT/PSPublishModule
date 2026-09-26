using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedScriptToolsSwitch_PreservesNestedAndPipelineFormatting(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "RuntimeSwitch", "FormatFunctions.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScriptToolsPipelineFormatting",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Format-Value", "Format-String" })
            Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
                entry => entry.Name == name).EmittedClrMethod);
        const string probe = """
            [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo('en-US')
            function Describe-Values($values) {
                [pscustomobject]@{values=@($values);types=@($values|ForEach-Object {$_.GetType().FullName})}|ConvertTo-Json -Compress
            }
            foreach($unit in 'KB','MB','GB','TB','PB') {
                Describe-Values @(1024,2097152,3221225472 | Format-Value -Unit $unit -Decimal 2)
            }
            Describe-Values @(1,1024,2097152 | Format-Value -Autodetect -Decimal 2)
            Describe-Values @(12.5,1000 | fv -AsCurrency)
            Describe-Values @(12.5,1000 | Format-Value -AsNumber -Decimal 3)
            Describe-Values @(12.5,1000 | Format-Value)
            foreach($case in 'Upper','Lower','Proper','Alternate','Toggle') {
                Describe-Values @('aBc-123','ŻółĆ','東京' | fs -Case $case)
                Describe-Values @('aBc-123','second' | Format-String -Case $case -Reverse -Replace @{'-'='_'})
            }
            Describe-Values @('plain','second' | Format-String)
            foreach($record in @('first','second' | Format-String -Case Upper -Verbose 4>&1)) {
                [pscustomobject]@{type=$record.GetType().FullName;value=$record.ToString()}|ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("System.Management.Automation.VerboseRecord", generated);
        Assert.Contains("FIRST", generated);
        Assert.Contains("SECOND", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedScriptToolsSwitch_PreservesOfflineFormattingWorkflows(string framework, string host)
    {
        var names = FindCompleteConversionWorkflow("PSScriptTools", "RuntimeSwitch", "FileNameTools.ps1");
        var bar = FindCompleteConversionWorkflow("PSScriptTools", "RuntimeSwitch", "New-ANSIBar.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(names) + Environment.NewLine + File.ReadAllText(bar), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScriptToolsRuntimeSwitch",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "New-ANSIBar", "New-CustomFileName" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
            Assert.True(unit.EmittedClrMethod, string.Join(" | ", unit.DiagnosticChain.Select(cause => cause.Message)));
            Assert.True(unit.RuntimeCommandRegions > 0);
        }
        const string probe = """
            $env:USERNAME='CompilerUser'
            $owner=(Get-Command New-CustomFileName).Module
            & $owner { function script:Get-Date { [datetime]::new(2024,2,29,17,6,7) } }
            foreach($case in 'Default','Upper','Lower') {
                foreach($template in 'Report.txt','Prefix-%YEAR-%month-%day-%username.txt','Item-%dayofweek-%hour24-%minute-%seconds.log') {
                    $values=@(New-CustomFileName -Template $template -Case $case)
                    [pscustomobject]@{case=$case;template=$template;values=$values;types=@($values|ForEach-Object {$_.GetType().FullName})}|ConvertTo-Json -Compress
                }
            }
            foreach($character in 'FullBlock','LightShade','MediumShade','DarkShade','BlackSquare','WhiteSquare') {
                foreach($gradient in $false,$true) {
                    $values=@(New-ANSIBar -Range @(16,17,18) -Character $character -Spacing 2 -Gradient:$gradient)
                    [pscustomobject]@{character=$character;gradient=$gradient;values=$values;types=@($values|ForEach-Object {$_.GetType().FullName})}|ConvertTo-Json -Compress
                }
            }
            @(nab -Range @(42) -Custom '#' -Spacing 3 -Gradient) | ConvertTo-Json -Compress
            @(New-ANSIBar -Range @(42) -Spacing 0) | ConvertTo-Json -Compress
            foreach($record in @(New-ANSIBar -Range @(1,2) -Gradient -Verbose 4>&1)) {
                [pscustomobject]@{type=$record.GetType().FullName;value=$record.ToString()}|ConvertTo-Json -Compress
            }
            foreach($parameters in @(@{Template='';Case='Default'},@{Template='valid';Case='invalid'})) {
                try { New-CustomFileName @parameters -ErrorAction Stop }
                catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category}|ConvertTo-Json -Compress }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("Prefix-2024-02-29-CompilerUser.txt", generated);
        Assert.Contains("System.Management.Automation.VerboseRecord", generated);
    }
}
