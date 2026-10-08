using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompoundPipelineSwitch_PreservesArrayRecordBoundaryThroughFallback(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-SwitchArrayRecord { ,@('one','two') }
            function Get-CompoundPipelineSwitch {
                [CmdletBinding()]
                param([ValidateRange(1,3)][int] $Seed = 2)
                switch (Get-SwitchArrayRecord | ForEach-Object { ,$_ }) {
                    'one two' { 'joined' }
                    'one' { 'first' }
                    'two' { 'second' }
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompoundPipelineSwitch",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = "@(Get-CompoundPipelineSwitch) | ConvertTo-Json -Compress";
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Contains("joined", original);
        Assert.Equal(original, generated);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            entry => entry.Name == "Get-CompoundPipelineSwitch");
        Assert.False(unit.EmittedClrMethod);
    }

    [Fact]
    public void RuntimeValuedSwitch_DoesNotBroadenRuntimeFreeStrictAdmission()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-ObjectSwitch { param([object] $Value) switch ($Value) { 'one' { return 1 } default { return 2 } } }",
            Path.Combine(Path.GetTempPath(), "runtime-switch-strict.ps1"));
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(strict.Emitted.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedRuntimeSwitch_PreservesOfflineConversionWorkflows(string framework, string host)
    {
        var encoding = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Resolve-Encoding.ps1");
        var size = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "Convert-ExchangeSize.ps1");
        var color = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "Convert-Color.ps1");
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine,
            new[] { encoding, size, color }.Select(File.ReadAllText)), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RuntimeSwitchWorkflows",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach ($name in @('Ascii','BigEndianUnicode','Unicode','UTF7','UTF8','UTF8BOM','UTF32','Default','OEM')) {
                $records=@(Resolve-Encoding -Name $name)
                foreach($record in $records) {
                    [pscustomobject]@{name=$name;count=$records.Count;type=$record.GetType().FullName;codePage=$record.CodePage;preamble=($record.GetPreamble() -join ',')} | ConvertTo-Json -Compress
                }
            }
            foreach ($to in @('Bytes','KB','MB','GB','TB')) {
                foreach ($display in @($false,$true)) {
                    $records=@(Convert-ExchangeSize -To $to -Size '49 GB (52,613,349,376 bytes)' -Display:$display -Precision 2)
                    [pscustomobject]@{to=$to;display=$display;count=$records.Count;values=$records;types=@($records | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Compress
                }
            }
            foreach ($value in @($null,'',' ')) { @(Convert-ExchangeSize -Size $value) | ConvertTo-Json -Compress }
            foreach ($rgb in @(@(0,0,0), @(51,51,204), @(255,128,1))) {
                @(Convert-Color -RGB $rgb) | ConvertTo-Json -Compress
            }
            foreach ($hex in @('000000','3333CC','FF8001')) {
                @(Convert-Color -HEX $hex) | ConvertTo-Json -Compress
            }
            $owner=(Get-Command Convert-ExchangeSize).Module
            & $owner {
                function script:Select-String {
                    param($Pattern, [switch] $AllMatches)
                    Remove-Variable To -Scope 1
                    Set-Variable To @('KB','MB') -Scope 1
                    [pscustomobject]@{Matches=[pscustomobject]@{Value='1,024'}}
                }
            }
            @(Convert-ExchangeSize -Size 'ignored' -To MB) | ConvertTo-Json -Compress
            Update-TypeData -TypeName System.String -MemberType ScriptMethod -MemberName ToUpperInvariant -Value { @() } -Force
            'empty=' + @(Resolve-Encoding -Name UTF8).Count
            Update-TypeData -TypeName System.String -MemberType ScriptMethod -MemberName ToUpperInvariant -Value { @('UTF8BOM','UTF8') } -Force
            @(Resolve-Encoding -Name UTF8) | ForEach-Object { 'many=' + ($_.GetPreamble() -join ',') }
            Remove-TypeData -TypeName System.String
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("empty=0", generated);
        Assert.Contains("many=239,187,191", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeRuntimeSwitch_PreservesItemsTransfersAndRestoration(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-RuntimeSwitch {
                [CmdletBinding()]
                param([object] $Value, [switch] $Mutate)
                $stamp = "$Value"
                $_ = 'outside'
                $switch = 'outside-iterator'
                switch ($Value) {
                    'one' { "first:$_"; if ($Mutate) { $_ = 'new' } }
                    'ONE' { "second:$PSItem"; continue }
                    'stop' { 'stop'; break }
                    'return' { return 'returned' }
                    'callbackBreak' { Stop-FromSwitch; 'unreachable' }
                    'callbackContinue' { Skip-FromSwitch; 'unreachable' }
                    'new' { 'mutated' }
                    default { "default:$_" }
                }
                "after:$_|$switch"
            }
            function Get-RuntimeSwitchCase {
                [CmdletBinding()]
                param([object] $Value)
                $stamp = "$Value"
                switch -CaseSensitive ($Value) { 'one' { 'lower' } 'ONE' { 'upper' } default { 'other' } }
            }
            function Stop-FromSwitch { break }
            function Skip-FromSwitch { continue }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RuntimeSwitchState",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        const string probe = """
            class SwitchText {
                static [int] $Count
                [string] ToString() { [SwitchText]::Count++; if ([SwitchText]::Count -eq 1) { return 'one' }; return 'new' }
            }
            $cases=@($null, 'one', 'missing', @(), @('one','missing','ONE'), @('one','stop','missing'), @('one','return','missing'), @('callbackBreak','missing'), @('callbackContinue','missing'))
            foreach($value in $cases) {
                [pscustomobject]@{values=@(Get-RuntimeSwitch -Value $value);sensitive=@(Get-RuntimeSwitchCase -Value $value)} | ConvertTo-Json -Compress -Depth 8
            }
            @(Get-RuntimeSwitch -Value @('one','missing') -Mutate) | ConvertTo-Json -Compress
            [SwitchText]::Count=0
            @(Get-RuntimeSwitch -Value ([SwitchText]::new())) | ConvertTo-Json -Compress
            'stringify=' + [SwitchText]::Count
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("after:outside|outside-iterator", generated);
    }
}
