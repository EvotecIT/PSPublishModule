namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeAs_PreservesPinnedDiskWorkflow(string framework, string host)
    {
        var path = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Computers", "Get-ComputerDisk.ps1");
        Assert.Equal("86631885b4b2d6b3d86f6723250e41b7e835653a74a2773e91c0a8806be5a4f1",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(path), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DiskAs", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1, System.Text.Json.JsonSerializer.Serialize(result.Manifest.UnitDispositionLedger));
        const string probe = """
            function global:Get-CimData {
                param($ComputerName,$Protocol,$Credential,$Class,$Properties)
                foreach($size in $global:diskSizes) {
                    [pscustomobject]@{PSComputerName='fixture';Index=1;Model='model';Caption='disk';SerialNumber='  serial  ';Description='drive';
                        MediaType='fixed';FirmwareRevision='v1';Partitions=2;Size=$size;PNPDeviceID='fixture-device'}
                }
            }
            foreach($sizes in @(@{v=@()},@{v=@(0)},@{v=@(1GB,2.5GB,$null)},@{v=@('invalid',[double]::MaxValue)})) {
                $global:diskSizes=$sizes.v
                foreach($all in $false,$true) {
                    $errors=@();$records=@(Get-ComputerDisk -ComputerName fixture -All:$all -ErrorVariable errors 2>$null)
                    [pscustomobject]@{all=$all;records=$records;errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId})} | ConvertTo-Json -Depth 8 -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "disk-as-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "disk-as-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(8, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeAs_PreservesConversionsDestinationFailuresAndContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeAs {
                [CmdletBinding()] param([object]$Value, [object]$Destination)
                $copy='prior'; $copy=$Value -as $Destination; return ,@($copy,'after')
            }
            function Read-NativeAsInt {
                [CmdletBinding()] param([object]$Value)
                $copy='prior'; $copy=$Value -as [int]; return ,@($copy,'after')
            }
            function Read-NativeAsCallbacks {
                [CmdletBinding()] param([object]$Source)
                $copy='prior'; $copy=$Source.Value -as $Source.Destination; return ,@($copy,'after')
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeAs", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3, System.Text.Json.JsonSerializer.Serialize(result.Manifest.UnitDispositionLedger));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Name.StartsWith("Read-NativeAs", StringComparison.Ordinal)), unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            $cases=@(@{value=$null},@{value=''},@{value='42'},@{value='bad'},@{value='2147483648'},
                @{value='0x2a'},@{value=2.5},@{value=@()},@{value=@(1)},@{value=@(1,2)},
                @{value=@('1','bad')},@{value=@(@(1,2),3)},@{value=[psobject]'12'},@{value=@{Name='sample'}})
            $targets=@([int],[string],[int[]],[datetime],[DayOfWeek],[type],'System.Int32','Missing.Type',$null)
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach($preference in 'Continue','Stop') {
                    for($target=0;$target -lt $targets.Count;$target++) {
                        for($index=0;$index -lt $cases.Count;$index++) {
                            $Error.Clear();$caught=$null;$records=@();$errors=@()
                            try {$records=@(Read-NativeAs -Value $cases[$index].value -Destination $targets[$target] -ErrorAction $preference -ErrorVariable errors 2>$null)}
                            catch {$caught=$_.FullyQualifiedErrorId}
                            $types=@($records | ForEach-Object { foreach($value in $_) { if($null -eq $value){'null'}else{$value.GetType().FullName} } })
                            [pscustomobject]@{culture=$culture;preference=$preference;target=$target;index=$index;records=$records;types=$types;caught=$caught;
                                errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId});globalErrors=@($Error | ForEach-Object {$_.FullyQualifiedErrorId})} | ConvertTo-Json -Depth 8 -Compress
                        }
                    }
                    foreach($case in $cases) { ,@(Read-NativeAsInt -Value $case.value -ErrorAction $preference) | ConvertTo-Json -Depth 8 -Compress }
                }
            }
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections.Generic;
            public sealed class AsCallbackValue {
                public static List<string> Trace = new List<string>();
                public static string Fault;
                public object Value { get { Trace.Add("left"); if(Fault=="left") throw new InvalidOperationException("left failed"); return this; } }
                public object Destination { get { Trace.Add("right"); if(Fault=="right") throw new InvalidOperationException("right failed"); return Fault=="target" ? (object)"Missing.Type" : typeof(int); } }
                public static explicit operator int(AsCallbackValue value) { Trace.Add("convert"); if(Fault=="convert") throw new FormatException("conversion failed"); return 42; }
            }
            '@
            foreach($fault in 'none','left','right','convert','target') {
                [AsCallbackValue]::Fault=$fault;[AsCallbackValue]::Trace.Clear()
                $records=@();$errors=@();$caught=$null
                try { $records=@(Read-NativeAsCallbacks -Source ([AsCallbackValue]::new()) -ErrorVariable errors 2>$null) }
                catch { $caught=$_.FullyQualifiedErrorId }
                [pscustomobject]@{fault=$fault;records=$records;trace=[AsCallbackValue]::Trace.ToArray();caught=$caught;
                    errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId})} | ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-as-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-as-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(565, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void AsConversionRemainsRejectedWithoutNativeFunctionBinding()
    {
        var document = PowerShellSourceParser.Parse("function Read-As { param([string]$Value); return $Value -as [int] }", TestPath("as.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Bound.Diagnostics, diagnostic => diagnostic.Code == PowerShellCompilationFeatureIds.ForOperator("as"));
    }
}
