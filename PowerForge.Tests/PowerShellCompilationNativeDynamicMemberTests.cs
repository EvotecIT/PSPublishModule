using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeMemberHosts))]
    public void NativeComputedMembers_PreserveStaticInstanceMutationAndNestedAccess(
        string framework, string host, int strictVersion)
    {
        using var fixture = ArtifactFixture.Create(
            (strictVersion == 0 ? "Set-StrictMode -Off" : "Set-StrictMode -Version 2") + Environment.NewLine + """
            function Read-DynamicMember { [CmdletBinding()] param([object]$Value,[object]$Name); 'before'; $Value.$Name; 'after' }
            function Read-DynamicStatic { [CmdletBinding()] param([object]$Name); return [System.Text.Encoding]::$Name.WebName }
            function Set-DynamicMember { [CmdletBinding()] param([object]$Value,[object]$Name,[object]$Data); $Value.$Name=$Data; return $Value.$Name }
            function Read-IndexedDynamicMember { [CmdletBinding()] param([object]$Value,[object]$Index,[object]$Name); return $Value[$Index].$Name }
            function Read-DynamicMemberIndex { [CmdletBinding()] param([object]$Value,[object]$Name,[object]$Index); return $Value.$Name[$Index] }
            function Read-DynamicMemberOrder { [CmdletBinding()] param([object]$Value,[object[]]$Names); $i=0; $result=$Value.($Names[$i++]); return "$result|$i" }
            function Set-DynamicMemberOrder { [CmdletBinding()] param([object]$Value,[object[]]$Names,[object]$Data); $i=0; $Value.($Names[$i++])=$Data; return "$($Value.Name)|$i" }
            function Read-DynamicReceiverNameOrder { [CmdletBinding()] param([object[]]$Value,[object[]]$Names); $i=0; $result=$Value[$i++].($Names[$i++]); return "$result|$i" }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeDynamicMembers",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries;
        Assert.Equal(8, result.Manifest.CompiledMethods);
        Assert.All(units.Where(unit => unit.Name != "<script>"), unit =>
        {
            Assert.True(unit.UsesNativeFunctionBinding, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
        });

        const string probe = """
            $thrower=[pscustomobject]@{}
            $thrower | Add-Member -MemberType ScriptProperty -Name Boom -Value { throw 'computed getter failed' }
            $cases=@(
                @{name='Read-DynamicMember';args=@{Value=[pscustomobject]@{Name='note'};Name='Name'}},
                @{name='Read-DynamicMember';args=@{Value=@{Name='key'};Name='Name'}},
                @{name='Read-DynamicMember';args=@{Value=$thrower;Name='Boom'}},
                @{name='Read-DynamicMember';args=@{Value=$null;Name='Missing'}},
                @{name='Read-DynamicMember';args=@{Value=[pscustomobject]@{Name='note'};Name='Missing'}},
                @{name='Read-DynamicStatic';args=@{Name='UTF8'}},
                @{name='Set-DynamicMember';args=@{Value=[pscustomobject]@{Name='old'};Name='Name';Data='new'}},
                @{name='Set-DynamicMember';args=@{Value=@{Name='old'};Name='Name';Data='new'}},
                @{name='Read-IndexedDynamicMember';args=@{Value=@([pscustomobject]@{Name='zero'},[pscustomobject]@{Name='one'});Index=1;Name='Name'}},
                @{name='Read-DynamicMemberIndex';args=@{Value=[pscustomobject]@{Items=@('zero','one')};Name='Items';Index=1}},
                @{name='Read-DynamicMemberOrder';args=@{Value=[pscustomobject]@{Name='once'};Names=@('Name','Missing')}},
                @{name='Set-DynamicMemberOrder';args=@{Value=[pscustomobject]@{Name='old'};Names=@('Name','Missing');Data='once'}},
                @{name='Read-DynamicReceiverNameOrder';args=@{Value=@([pscustomobject]@{Name='ordered'});Names=@('Wrong','Name')}}
            )
            foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                for ($index=0;$index -lt $cases.Count;$index++) {
                    $Error.Clear(); $caught=$null; $records=@(); $emitted=@()
                    $case=$cases[$index]; $arguments=$case.args
                    try { $records=@(& $case.name @arguments -ErrorAction $preference -OutVariable emitted 2>$null) }
                    catch { $caught=$_.FullyQualifiedErrorId }
                    [pscustomobject]@{case=$index;preference=$preference;records=$records;emitted=@($emitted);alias=$arguments.Value.Name;caught=$caught;
                        errors=@($Error | ForEach-Object { [pscustomobject]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;message=$_.Exception.Message} })} |
                        ConvertTo-Json -Compress -Depth 12
                }
            }
            for ($round=0;$round -lt 2;$round++) {
                if ($round -gt 0) { Get-Module | Where-Object Path -EQ $modulePath | Remove-Module; Import-Module $modulePath -Force }
                $stopped=@(Read-DynamicMember -Value ([pscustomobject]@{Name='stop'}) -Name Name | Select-Object -First 1)
                $afterStop=@(Read-DynamicStatic -Name UTF8)
                [pscustomobject]@{round=$round;stopped=$stopped;afterStop=$afterStop} | ConvertTo-Json -Compress -Depth 5
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; Import-Module $modulePath; " + probe,
            fixture.RootPath, "original-native-dynamic-members");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; Import-Module $modulePath; " + probe,
            fixture.RootPath, "compiled-native-dynamic-members");

        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(54, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8)
            .Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    public void ComputedMembers_RemainOutsideStrictRuntimeFreeCompilation()
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-DynamicMember { param([object]$Value,[object]$Name); $Value.$Name }
            """, ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Strict));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, finding => finding.FeatureId == "syntax.memberexpression");
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_PinnedComputedMemberWorkflowsUseOneNativeContract()
    {
        var sources = new[]
        {
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "Email", "Send-Email.ps1"),
                Hash: "8889e968a67998b9827d1f6393a1849618c0490c6ad1660845193b3dc3add868", Name: "Send-Email", Emitted: false),
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "SQL", "New-SqlTableMapping.ps1"),
                Hash: "c3463622ac542faf44ad2a42b5b35b78b2f3dee4866b836237c8c038c23f9515", Name: "New-SqlTableMapping", Emitted: true),
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "XML", "Set-XML.ps1"),
                Hash: "4cbc1565bc39d1a4803143f9151f3b2c702495c25b49709fbac1e2a7ac285372", Name: "Set-XML", Emitted: true)
        };
        Assert.All(sources, source => Assert.Equal(source.Hash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source.Path))).ToLowerInvariant()));

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            sources.Select(static source => source.Path), "PowerForge.Compiled", "PinnedComputedMemberMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        foreach (var source in sources)
        {
            var method = typed.Methods.SingleOrDefault(method => method.SourceName == source.Name);
            Assert.True(source.Emitted == (method is not null), source.Name + ": " +
                string.Join(" | ", typed.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            if (source.Emitted) Assert.NotNull(method!.NativeFunctionBinding);
        }
        Assert.Contains("ReadDynamicMember", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("AssignTarget", typed.SourceCode, StringComparison.Ordinal);
        Assert.DoesNotContain(typed.Diagnostics, diagnostic => diagnostic.FeatureId == "syntax.memberexpression");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$value = @{}", "$value.$Name = @{}")]
    [InlineData("$value = [Collections.ArrayList]::new()", "$value.$Name = [Collections.ArrayList]::new()")]
    [InlineData("$value = [Collections.ArrayList]::new()", "$value[$Name] = [Collections.ArrayList]::new()")]
    public void Transpile_ReceiverMutationNeverProjectsAsWholeVariableAssignment(
        string initializer, string receiverMutation)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function Set-ReceiverMutation {
                param([string]$Name)
                {{initializer}}
                {{receiverMutation}}
                data MutationBarrier { }
                return ,$value
            }
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ReceiverMutationMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var mutationOffset = File.ReadAllText(fixture.ScriptPath).IndexOf(receiverMutation, StringComparison.Ordinal);

        Assert.DoesNotContain(typed.PromotedRegions, region =>
            region.StartOffset <= mutationOffset && region.EndOffset > mutationOffset);
        Assert.DoesNotContain(typed.SourceCode, " = new global::System.Collections.Hashtable();" + Environment.NewLine +
            "            value = new global::System.Collections.Hashtable();", StringComparison.Ordinal);
    }
}
