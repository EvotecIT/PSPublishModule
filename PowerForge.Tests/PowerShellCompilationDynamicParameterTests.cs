using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void DynamicParameters_DoNotAdmitTheCleanCompatibilityRoute()
    {
        var parsed = PowerShellSourceParser.Parse("function Read-DynamicClean { param() dynamicparam { $null } end { 1 } clean { 2 } }",
            Path.Combine(Path.GetTempPath(), "dynamic-clean.psm1"));
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { parsed }, "net10.0",
            PowerShellCompilationCapabilities.HybridModule).Emitted.Methods);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void DynamicParameters_RequireNativeHostCapabilities(string framework)
    {
        var parsed = PowerShellSourceParser.Parse("function Read-Dynamic { param([string]$Mode) dynamicparam { $null } end { $Mode } }",
            Path.Combine(Path.GetTempPath(), "dynamic-capability.psm1"));
        foreach(var capabilities in new[]{PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.BinaryModule,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes})
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[]{parsed},framework,capabilities).Emitted.Methods);
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void DynamicParameters_PreserveBindingTimingAndSharedInvocationStorage(string framework, string host)
    {
        const string source = """
            $script:DiscoveryCalls=0
            function Read-OfflineDynamicParameter {
                [CmdletBinding()]param([string]$Mode='enabled',[Parameter(ValueFromPipeline)][string]$Value)
                dynamicparam {
                    $script:DiscoveryCalls++
                    $discoveryValue='discovered:'+$Mode
                    if($Mode -eq 'fail'){throw 'offline discovery failure'}
                    if($Mode -eq 'enabled'){
                        $attributes=[Collections.ObjectModel.Collection[Attribute]]::new()
                        $attributes.Add([Management.Automation.ParameterAttribute]::new())
                        $attributes.Add([Management.Automation.AliasAttribute]::new('choice'))
                        $attributes.Add([Management.Automation.ValidateSetAttribute]::new('one','two'))
                        $parameter=[Management.Automation.RuntimeDefinedParameter]::new('Selection',[string],$attributes)
                        $dictionary=[Management.Automation.RuntimeDefinedParameterDictionary]::new()
                        $dictionary.Add('Selection',$parameter)
                        return $dictionary
                    }
                }
                begin {$beginValue=$discoveryValue}
                process {
                    [pscustomobject]@{mode=$Mode;selection=$PSBoundParameters['Selection'];direct=$Selection;discovery=$discoveryValue;begin=$beginValue;calls=$script:DiscoveryCalls;value=$Value}
                }
            }
            function Read-OfflineDiscoveryCount { [CmdletBinding()]param(); $script:DiscoveryCalls }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DynamicParameters", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.True(built.Manifest!.UnitDispositionLedger!.Entries.Single(entry => entry.Name == "Read-OfflineDynamicParameter").EmittedClrMethod);
        const string probe = """
            [pscustomobject]@{case='import';calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Compress
            foreach($mode in 'enabled','disabled','fail'){
                foreach($selection in 'one','bad'){
                    try {
                        $records=@(Read-OfflineDynamicParameter -Mode $mode -choice $selection -ErrorAction Stop)
                        [pscustomobject]@{case='invoke';mode=$mode;selection=$selection;records=$records;calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Depth 6 -Compress
                    }catch{
                        [pscustomobject]@{case='failure';mode=$mode;selection=$selection;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Compress
                    }
                }
            }
            [pscustomobject]@{case='disabled';records=@(Read-OfflineDynamicParameter -Mode disabled);calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Depth 6 -Compress
            foreach($mode in 'enabled','disabled'){
                $command=Get-Command Read-OfflineDynamicParameter -ArgumentList $mode
                [pscustomobject]@{case='lookup';mode=$mode;selection=$command.Parameters.ContainsKey('Selection');calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Compress
            }
            [pscustomobject]@{case='pipeline';records=@('a','b'|Read-OfflineDynamicParameter -Mode enabled -choice two);calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Depth 6 -Compress
            Import-Module (Get-Command Read-OfflineDynamicParameter).Module.Path -Force
            [pscustomobject]@{case='reimport';calls=(Read-OfflineDiscoveryCount)}|ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        Assert.Equal(12, original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"case\":\"import\",\"calls\":0", original);
        Assert.Contains("discovered:enabled", original);
        Assert.Contains("ParameterArgumentValidationError", original);
        Assert.Contains("GetDynamicParametersException", original);
        Assert.Contains("\"mode\":\"enabled\",\"selection\":true", original);
        Assert.Contains("\"mode\":\"disabled\",\"selection\":false", original);
        Assert.Contains("\"value\":\"a\"", original);
        Assert.Contains("\"value\":\"b\"", original);
        // Dynamic values belong to PSBoundParameters. An unqualified read still
        // observes the caller's same-name variable, including after the last loop case.
        Assert.Contains("\"direct\":\"one\"", original);
        Assert.Contains("\"selection\":\"two\",\"direct\":\"bad\"", original);
        Assert.Contains("\"case\":\"reimport\",\"calls\":0", original);
    }

    [Fact]
    public void DynamicParameters_PreserveHostedExecutableBinding()
    {
        const string source = """
            function Read-DynamicEntry {
                [CmdletBinding()]param([string]$Mode)
                dynamicparam {
                    $discovery='from:'+ $Mode
                    if($Mode -eq 'fail'){throw 'offline executable discovery failure'}
                    if($Mode -eq 'enabled'){
                        $attributes=[Collections.ObjectModel.Collection[Attribute]]::new()
                        $attributes.Add([Management.Automation.ParameterAttribute]::new())
                        $attributes.Add([Management.Automation.AliasAttribute]::new('choice'))
                        $attributes.Add([Management.Automation.ValidateSetAttribute]::new('one','two'))
                        $parameter=[Management.Automation.RuntimeDefinedParameter]::new('Selection',[string],$attributes)
                        $dictionary=[Management.Automation.RuntimeDefinedParameterDictionary]::new()
                        $dictionary.Add('Selection',$parameter)
                        $dictionary
                    }
                }
                end { $PSBoundParameters['Selection'] + ':' + $discovery }
            }
            foreach($mode in 'enabled','disabled','fail'){
                foreach($choice in 'one','bad'){
                    try {
                        $records=@(Read-DynamicEntry -Mode $mode -choice $choice -ErrorAction Stop)
                        [pscustomobject]@{mode=$mode;choice=$choice;records=$records}|ConvertTo-Json -Compress
                    }catch{
                        [pscustomobject]@{mode=$mode;choice=$choice;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}|ConvertTo-Json -Compress
                    }
                }
            }
            """;
        using var fixture = ArtifactFixture.Create(source);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DynamicEntry", PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UnitDispositionLedger!.Entries.Single(entry => entry.Name == "Read-DynamicEntry").EmittedClrMethod);
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        Assert.Equal(0, generated.ExitCode);
        Assert.Contains("one:from:enabled", generated.StandardOutput);
        Assert.Contains("ParameterArgumentValidationError", generated.StandardOutput);
        Assert.Contains("NamedParameterNotFound", generated.StandardOutput);
        Assert.Contains("GetDynamicParametersException", generated.StandardOutput);
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void DynamicParameters_PreservePinnedHelpAndTreeWorkflows(string framework, string host)
    {
        foreach (var workflow in new[]
        {
            (File: "Copy-HelpExample.ps1", Name: "Copy-HelpExample", Observer: "ObserveCopyHelp.ps1", Count: 6,
                Hash: "50F8151458AADC0581A65C51A5FA72769DBF489131FD9075A645545585C760E5"),
            (File: "ShowTree.ps1", Name: "Show-Tree", Observer: "ObserveTree.ps1", Count: 8,
                Hash: "247ED38488726D4FFDD93A7A6AF81779FFEC825585513A78ED812CDDCFCA5548")
        })
        {
            var source = FindCompleteConversionWorkflow("PSScriptTools", "DynamicParameters", workflow.File);
            var observer = FindCompleteConversionWorkflow("PSScriptTools", "DynamicParameters", workflow.Observer);
            Assert.Equal(workflow.Hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
            using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath, fixture.OutputPath, "Generated.DynamicWorkflow", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
            Assert.Equal(workflow.Name, unit.Name);
            Assert.True(unit.EmittedClrMethod);
            var treeRoot = Path.Combine(fixture.RootPath, "tree-owned");
            Directory.CreateDirectory(Path.Combine(treeRoot, "FolderA"));
            Directory.CreateDirectory(Path.Combine(treeRoot, "FolderB"));
            File.WriteAllText(Path.Combine(treeRoot, "owned.txt"), "owned");
            string Observe(string module)
            {
                var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-File", observer, "-ModulePath", module };
                if (workflow.Name == "Show-Tree") arguments.AddRange(new[] { "-TreeRoot", treeRoot });
                var proof = RunProcess(host, arguments.ToArray());
                Assert.True(proof.ExitCode == 0, proof.StandardOutput + proof.StandardError);
                Assert.True(string.IsNullOrWhiteSpace(proof.StandardError), proof.StandardError);
                return proof.StandardOutput;
            }
            var original = Observe(fixture.ScriptPath);
            Assert.Equal(original, Observe(result.ArtifactPath!));
            var records = original.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => System.Text.Json.JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(workflow.Count, records.Length);
                Assert.Equal(System.Text.Json.JsonValueKind.Null, records[0].RootElement.GetProperty("caught").ValueKind);
                if (workflow.Name == "Copy-HelpExample")
                {
                    Assert.Equal(new[] { "help:Get-Date:True", "grid:Select one or more code samples to copy:True", "clipboard:'owned sample'" },
                        records[0].RootElement.GetProperty("trace").EnumerateArray().Select(item => item.GetString()).ToArray());
                    Assert.Equal(System.Text.Json.JsonValueKind.Null, records[1].RootElement.GetProperty("caught").ValueKind);
                    Assert.Contains("offline help failure", records[3].RootElement.GetProperty("caught").GetProperty("message").GetString());
                }
                else
                {
                    Assert.Contains("FolderA", records[0].RootElement.GetProperty("records").ToString());
                    Assert.StartsWith("NamedParameterNotFound", records[6].RootElement.GetProperty("caught").GetProperty("id").GetString());
                    Assert.StartsWith("GetDynamicParametersException", records[7].RootElement.GetProperty("caught").GetProperty("id").GetString());
                }
            }
            finally { foreach (var record in records) record.Dispose(); }
        }
    }
}
