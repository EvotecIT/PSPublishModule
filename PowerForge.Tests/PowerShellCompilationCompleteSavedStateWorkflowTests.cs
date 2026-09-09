namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedSavedStatePreservesConversionAndAliasing(string framework, string host)
        => AssertPinnedSavedStateWorkflow(framework, host, fullModule: false);

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedSavedStateInFullModule(string framework, string host)
        => AssertPinnedSavedStateWorkflow(framework, host, fullModule: true);

    private static void AssertPinnedSavedStateWorkflow(string framework, string host, bool fullModule)
    {
        var sources = new[] {
            ("Import-ComputersData.ps1", "98bd6c47fd4adb1ee6b74901b8cc2330912786d24d03067356f4229a025b1324"),
            ("Convert-ListProcessed.ps1", "443dc2a79c156d4b15c9e17dc85068113433096bef1805edb0daf9adaac06785")
        };
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine,
            sources.Select(source => ". \"$PSScriptRoot/" + source.Item1 + "\"")), ".psm1");
        foreach (var source in sources)
        {
            var path = FindCompleteConversionWorkflow("CleanupMonster", source.Item1);
            Assert.Equal(source.Item2, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            if (!fullModule)
                File.Copy(path, Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, source.Item1));
        }
        if (fullModule)
        {
            var snapshot = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "CleanupMonster.psm1");
            var snapshotRoot = Path.GetDirectoryName(snapshot)!;
            var hashes = Path.Combine(snapshotRoot, "SHA256SUMS.txt");
            Assert.Equal("efe6355df50a57bb06472c45378242b6bbff4557d0be5d6c10edd8d21ee5b530",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hashes))).ToLowerInvariant());
            var entries = File.ReadAllLines(hashes);
            Assert.Equal(68, entries.Length);
            foreach (var entry in entries)
                Assert.Equal(entry[..64], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(snapshotRoot, entry[66..])))).ToLowerInvariant());
            File.Copy(snapshot, fixture.ScriptPath, overwrite: true);
            var files = Directory.GetFiles(snapshotRoot, "*.ps1", SearchOption.AllDirectories);
            Assert.Equal(67, files.Length);
            foreach (var source in files)
            {
                var destination = Path.Combine(fixture.RootPath, Path.GetRelativePath(snapshotRoot, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
        }
        const string probe = """
            $module=Import-Module $modulePath -PassThru -Force
            & $module {
                $script:Trace=[Collections.Generic.List[string]]::new()
                function script:Get-Date { [datetime]'2026-01-20' }
                function script:Test-Path { [CmdletBinding()]param($LiteralPath) $script:Trace.Add('test'); $script:Mode -ne 'missing' }
                function script:Import-Clixml { [CmdletBinding()]param($LiteralPath) $script:Trace.Add('read'); if($script:Mode -eq 'read-failure'){throw 'read-failure'}; $script:Stored }
                function script:Write-Color { param($Text,$Color) $script:Trace.Add(($Text -join '')) }
                function script:ConvertFrom-DistinguishedName { param($DistinguishedName,[switch]$ToDomainCN) $script:Trace.Add('convert:'+ $DistinguishedName); if($script:Mode -eq 'conversion-failure'){throw 'conversion-failure'}; 'example.test' }
                foreach($mode in 'missing','empty','legacy','modern','existing-property','no-date','read-failure','conversion-failure','invalid-history') {
                    $script:Mode=$mode
                    $script:Trace.Clear()
                    $pending=[ordered]@{}
                    $history=[Collections.ArrayList]::new()
                    $null=$history.Add('previous')
                    $item=[pscustomobject]@{SamAccountName='host$';DistinguishedName='CN=host,DC=example,DC=test';ActionDate=[datetime]'2026-01-17'}
                    if($mode -eq 'no-date'){$item.ActionDate=$null}
                    if($mode -eq 'existing-property'){$item | Add-Member TimeOnPendingList 99}
                    if($mode -ne 'empty') {
                        $key=if($mode -eq 'modern'){'host$@example.test'}else{$item.DistinguishedName}
                        $pending[$key]=$item
                    }
                    $script:Stored=@{PendingDeletion=$pending;History=$history}
                    if($mode -eq 'invalid-history'){$script:Stored.History='wrong'}
                    $export=@{}
                    for($repeat=0;$repeat -lt 2;$repeat++) {
                        $result=Import-ComputersData -DataStorePath 'isolated-state' -Export $export
                        [pscustomobject]@{mode=$mode;repeat=$repeat;result=$result;state=$script:Stored;export=$export;trace=@($script:Trace);pendingKeys=@($pending.Keys);pendingType=$pending.GetType().FullName;sameState=[object]::ReferenceEquals($result,$pending);sameHistory=[object]::ReferenceEquals($export.History,$history)} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "saved-state-original");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Equal(18, original.StandardOutput.Split('\n').Count(line => line.StartsWith("{", StringComparison.Ordinal)));
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        if (fullModule)
            Assert.Equal(68, resolved.SourceFiles.Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "Generated.CompleteSavedState", resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Import-ComputersData", "Convert-ListProcessed" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.False(unit.RetainedHostedSource);
        }
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "saved-state-compiled");
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actual = compiled.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected.Length, actual.Length);
        // Hashtable enumeration can differ between host processes. Compare JSON objects by key;
        // ordered dictionary keys and all stream records remain explicitly ordered arrays.
        foreach (var pair in expected.Zip(actual))
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(pair.First), System.Text.Json.Nodes.JsonNode.Parse(pair.Second)),
                "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
