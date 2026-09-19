using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private static readonly (string[] Path, string Sha256, string Name)[] UnknownForEachWorkflows =
    {
        (new[] { "PSSharedGoods", "FullModule", "Public", "Programs", "Find-MyProgramData.ps1" },
            "53829f4358830a4e365edb32025cc605cb5762a1eeee59dc33cf1bcfa1a5a14f", "Find-MyProgramData"),
        (new[] { "PSSharedGoods", "FullModule", "Public", "Objects", "Get-Types.ps1" },
            "074378e3bec1c9a04e1f65774190b10723108cfadf83786a861456915c07681c", "Get-Types"),
        (new[] { "PSSharedGoods", "FullModule", "Public", "Vizualization", "Show-DataInVerbose.ps1" },
            "b2e8d5a4c5be9db380e4de1cd2b75194a16307d0c0aefdeccc6700d68fb9e6a7", "Show-DataInVerbose")
    };

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeUnknownForEach_PreservesThreePinnedWorkflowsAcrossHosts(string framework, string host)
    {
        var sources = UnknownForEachWorkflows.Select(workflow =>
        {
            var path = FindCompleteConversionWorkflow(workflow.Path);
            Assert.Equal(workflow.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            return File.ReadAllText(path);
        });
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeUnknownForEach",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var workflow in UnknownForEachWorkflows)
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
                entry => entry.Name == workflow.Name);
            Assert.True(unit.EmittedClrMethod, workflow.Name + ": " +
                string.Join(" | ", unit.DiagnosticChain.Select(static diagnostic => diagnostic.Message)));
            Assert.True(unit.UsesNativeFunctionBinding, workflow.Name);
            Assert.False(unit.RetainedHostedSource, workflow.Name);
        }

        const string probe = """
            for ($round = 0; $round -lt 2; $round++) {
                if ($round -gt 0) { Remove-Module $module.Name; $module = Import-Module $modulePath -PassThru }
                $find = @(
                    Find-MyProgramData -Data @('Alpha 10','Beta 20') -FindText '*Beta*'
                    Find-MyProgramData -Data @('Alpha 10') -FindText '*missing*'
                    Find-MyProgramData -Data $null -FindText '*'
                    Find-MyProgramData -Data 'Solo 30' -FindText 'Solo*'
                )
                $types = @(
                    Get-Types -Types ([DayOfWeek])
                    Get-Types -Types @([ConsoleColor],[EnvironmentVariableTarget])
                ) | ForEach-Object { $_.GetType().FullName + ':' + [string]$_ }
                $record = [pscustomobject]@{ Name = 'Ada'; Count = 2 }
                $verbose = @(Show-DataInVerbose -Object $record -Verbose 4>&1 | ForEach-Object { $_.ToString() })
                $many = @(Show-DataInVerbose -Object @($record, [pscustomobject]@{ Name = 'Lin' }) -Verbose 4>&1 |
                    ForEach-Object { $_.ToString() })
                [pscustomobject]@{round=$round;find=$find;types=$types;verbose=$verbose;many=$many} |
                    ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "native-unknown-foreach-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "native-unknown-foreach-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDictionaryOutput_UsesHostLiteralKeyComparer(string framework, string host)
    {
        const string source = """
            function Read-CultureMap {
                [CmdletBinding()] param([switch]$AsMap)
                $map = [ordered]@{ 'ß' = 'sharp'; 'I' = 'latin' }
                foreach ($key in $map.Keys) { $null = $key }
                if ($AsMap) { $map } else { $map['ss']; $map['i'] }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        File.WriteAllText(fixture.ScriptPath, source, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCultureMap",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod,
            string.Join(" | ", unit.DiagnosticChain.Select(static diagnostic => diagnostic.Message)));
        Assert.True(unit.UsesNativeFunctionBinding);
        const string probe = """
            $oldCulture = [Threading.Thread]::CurrentThread.CurrentCulture
            try {
                [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]'tr-TR'
                $records = @(Read-CultureMap -AsMap)
                [pscustomobject]@{ count=$records.Count; type=$records[0].GetType().FullName;
                    keys=@($records[0].Keys); lookup=@(Read-CultureMap) } |
                    ConvertTo-Json -Depth 5 -Compress
            } finally { [Threading.Thread]::CurrentThread.CurrentCulture = $oldCulture }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "culture-map-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "culture-map-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeHashtableOutput_PreservesLiteralAndBorrowedMapIdentity(string framework, string host)
    {
        const string source = """
            function Read-CultureHashtable {
                [CmdletBinding()] param([switch]$AsMap, [hashtable]$Borrowed)
                $map = @{ 'ß' = 'sharp'; 'I' = 'latin' }
                foreach ($key in $map.Keys) { $null = $key }
                $map['Extra'] = 'added'
                if ($Borrowed) { $map = $Borrowed }
                if ($AsMap) { $map } else { $map['ss']; $map['i'] }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        File.WriteAllText(fixture.ScriptPath, source, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeHashtableOutput",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod,
            string.Join(" | ", unit.DiagnosticChain.Select(static diagnostic => diagnostic.Message)));
        Assert.True(unit.UsesNativeFunctionBinding);
        const string probe = """
            $oldCulture = [Threading.Thread]::CurrentThread.CurrentCulture
            try {
                [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]'tr-TR'
                $borrowed = @{ Name = 'existing' }
                $created = @(Read-CultureHashtable -AsMap)
                $returned = @(Read-CultureHashtable -AsMap -Borrowed $borrowed)
                [pscustomobject]@{ createdCount=$created.Count; createdType=$created[0].GetType().FullName;
                    createdKeys=@($created[0].Keys | Sort-Object); createdExtra=$created[0]['Extra'];
                    lookup=@(Read-CultureHashtable);
                    borrowedSame=[object]::ReferenceEquals($returned[0],$borrowed) } |
                    ConvertTo-Json -Depth 5 -Compress
            } finally { [Threading.Thread]::CurrentThread.CurrentCulture = $oldCulture }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "hashtable-output-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "hashtable-output-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"borrowedSame\":true", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DictionaryOutput_KeepsNonNativeHashtableEscapeHosted()
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-UnqualifiedMap {
                [CmdletBinding()] param()
                $map = @{ Name = 'value' }
                $map
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.UnqualifiedMap",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.False(unit.EmittedClrMethod);
        Assert.True(unit.RetainedHostedSource);
        Assert.Contains(unit.DiagnosticChain, static diagnostic =>
            diagnostic.Message.Contains("dictionary literals are lookup-only locals", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDictionaryOutput_PreservesCollectedMapCardinality(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-CollectedMap {
                [CmdletBinding()] param()
                $map = [ordered]@{ Name = 'value' }
                foreach ($key in $map.Keys) { $null = $key }
                @($map)
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CollectedMap",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod,
            string.Join(" | ", unit.DiagnosticChain.Select(static diagnostic => diagnostic.Message)));
        Assert.True(unit.UsesNativeFunctionBinding);
        const string probe = """
            $records = @(Read-CollectedMap)
            [pscustomobject]@{count=$records.Count;type=$records[0].GetType().FullName;
                keys=@($records[0].Keys)} | ConvertTo-Json -Depth 5 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "collected-map-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "collected-map-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"count\":1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeUnknownForEach_EmitsOrderedMapLoopWithOriginalRecords(string framework, string host)
    {
        var path = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Get-ProtocolDefaults.ps1");
        Assert.Equal("ad58cb0a843a952dcc6deb530c0afab650397f02ff8d7aed3ac419171602ee5a",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(path), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.OrderedMapForeach",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod,
            string.Join(" | ", unit.DiagnosticChain.Select(static diagnostic => diagnostic.Message)));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);

        const string probe = """
            function Describe-Record($record) {
                $keys = if ($record -is [System.Collections.IDictionary]) { @($record.Keys) }
                    else { @($record.PSObject.Properties.Name) }
                [pscustomobject]@{ type=$record.GetType().FullName; keys=$keys;
                    values=@($keys | ForEach-Object { [string]$record.$_ }) }
            }
            for ($round = 0; $round -lt 2; $round++) {
                if ($round -gt 0) { Remove-Module $module.Name; $module = Import-Module $modulePath -PassThru }
                foreach ($version in 'Windows Server 2022','windows server 2022','No such Windows') {
                    $records = @(Get-ProtocolDefaults -WindowsVersion $version)
                    [pscustomobject]@{ round=$round; version=$version; count=$records.Count;
                        records=@($records | ForEach-Object { Describe-Record $_ }) } |
                        ConvertTo-Json -Depth 8 -Compress
                }
                $records = @(Get-ProtocolDefaults -AsList)
                [pscustomobject]@{ round=$round; mode='list'; count=$records.Count;
                    records=@($records | ForEach-Object { Describe-Record $_ }) } |
                    ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "ordered-map-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "ordered-map-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(8, original.StandardOutput.Split('\n').Count(static line => !string.IsNullOrWhiteSpace(line)));
        Assert.Contains("\"count\":26", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("System.Collections.Specialized.OrderedDictionary", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeUnknownForEach_PreservesLazyEnumeratorFailuresAndAuthoredCleanup(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Deferred {
                [CmdletBinding()] param([object]$Items, [object]$Trace)
                try {
                    foreach ($Item in $Items) {
                        $null = $Trace.Add('body:' + [string]$Item)
                        $Item
                        if ($Item -eq 'break') { break }
                    }
                    'after'
                } finally { $null = $Trace.Add('finally') }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeDeferredForEach",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod, string.Join(" | ", unit.DiagnosticChain.Select(static d => d.Message)));
        Assert.True(unit.UsesNativeFunctionBinding);

        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class DeferredProbe : IEnumerable {
                public static string Events = "";
                public string Failure;
                public int Count;
                public IEnumerator GetEnumerator() {
                    Events += "get;";
                    if (Failure == "get") throw new InvalidOperationException("get failed");
                    return new Cursor(this);
                }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly DeferredProbe owner;
                    private int index = -1;
                    public Cursor(DeferredProbe owner) { this.owner = owner; }
                    public bool MoveNext() {
                        index++; Events += "move:" + index + ";";
                        if (owner.Failure == "move" && index == 1) throw new InvalidOperationException("move failed");
                        return index < owner.Count;
                    }
                    public object Current { get {
                        Events += "current:" + index + ";";
                        if (owner.Failure == "current" && index == 1) throw new InvalidOperationException("current failed");
                        return index == 0 ? "first" : "break";
                    } }
                    public void Reset() { throw new NotSupportedException(); }
                    public void Dispose() { Events += "dispose;"; }
                }
            }
            '@
            function Describe-Record($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    return 'error:' + $item.FullyQualifiedErrorId + ':' + $item.Exception.Message
                }
                return 'value:' + [string]$item
            }
            foreach ($failure in 'none','get','move','current') {
                foreach ($count in 0,2) {
                    foreach ($action in 'Continue','Stop') {
                        $inputValue = [DeferredProbe]::new()
                        $inputValue.Failure = $failure
                        $inputValue.Count = $count
                        [DeferredProbe]::Events = ''
                        $trace = [Collections.Generic.List[string]]::new()
                        $records = [Collections.Generic.List[string]]::new()
                        $Error.Clear(); $caught = $null
                        try {
                            Read-Deferred -Items $inputValue -Trace $trace -ErrorAction $action 2>&1 |
                                ForEach-Object { $records.Add((Describe-Record $_)) }
                        } catch { $caught = Describe-Record $_ }
                        [pscustomobject]@{failure=$failure;count=$count;action=$action;
                            records=$records.ToArray();caught=$caught;trace=$trace.ToArray();
                            events=[DeferredProbe]::Events} | ConvertTo-Json -Compress -Depth 6
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-deferred-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-deferred-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(16, original.StandardOutput.Split('\n').Count(static line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
