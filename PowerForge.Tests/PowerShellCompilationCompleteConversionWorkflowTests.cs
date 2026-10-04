using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> CompleteConversionWorkflowHosts()
    {
        foreach (var configuration in StatementErrorHosts())
            foreach (var mode in new[] { PowerShellCompilationMode.Strict, PowerShellCompilationMode.Hybrid })
                yield return new[] { configuration[0], configuration[1], mode };
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(CompleteConversionWorkflowHosts))]
    public void CompleteWorkflow_PinnedBinaryToStringPreservesBindingAndOutput(string framework, string host, PowerShellCompilationMode mode)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "Convert-BinaryToString.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompleteConversion", PowerShellCompilationArtifactKind.BinaryModule,
            mode, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.Equal(1, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class ConversionInput : IEnumerable {
                public static string Trace = "";
                public string Failure;
                public IEnumerator GetEnumerator() { Trace += "get;"; return new Cursor(Failure); }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly string failure;
                    private int index = -1;
                    internal Cursor(string value) { failure = value; }
                    public bool MoveNext() {
                        Trace += "move;"; index++;
                        if (failure == "move" && index == 1) throw new InvalidOperationException("input move failed");
                        return index < 2;
                    }
                    public object Current { get {
                        Trace += "current;";
                        if (failure == "current" && index == 1) throw new InvalidOperationException("input current failed");
                        return index == 0 ? 65 : 0;
                    } }
                    public void Reset() { throw new NotSupportedException(); }
                    public void Dispose() {
                        Trace += "dispose;";
                        if (failure == "dispose") throw new InvalidOperationException("input disposal failed");
                    }
                }
            }
            '@
            function Describe-Record($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{ error=$item.FullyQualifiedErrorId; type=$item.Exception.GetType().FullName; category=[string]$item.CategoryInfo.Category; message=$item.Exception.Message }
                } elseif ($item -is [Management.Automation.IContainsErrorRecord]) {
                    [pscustomobject]@{ type=$item.GetType().FullName; record=(Describe-Record $item.ErrorRecord) }
                } else { [pscustomobject]@{ type=$item.GetType().FullName; value=$item } }
            }
            $cases=@(
                @{name='null';value=$null},
                @{name='empty';value=[byte[]]@()},
                @{name='singleton';value=[byte[]]@(65)},
                @{name='pair';value=[byte[]]@(65,0)},
                @{name='odd';value=[byte[]]@(65,0,66)},
                @{name='malformed';value=[byte[]]@(0,216)},
                @{name='unicode';value=[Text.Encoding]::Unicode.GetBytes('zażółć 東京')},
                @{name='nested';value=@([byte[]]@(65,0),[byte[]]@(66,0))},
                @{name='negative';value=@(-1,0)},
                @{name='overflow';value=@(256,0)},
                @{name='invalid';value='bad'},
                @{name='convertible';value=@('65','0')})
            foreach ($case in $cases) {
                foreach ($binding in 'Binary','Bin','positional') {
                    $records=[Collections.Generic.List[object]]::new(); $Error.Clear(); $faults=@()
                    try {
                        if ($binding -eq 'positional') { Convert-BinaryToString $case.value -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                        else {
                            $parameters=@{}; $parameters[$binding]=$case.value
                            Convert-BinaryToString @parameters -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) }
                        }
                    } catch { [void]$records.Add((Describe-Record $_)) }
                    [pscustomobject]@{ case=$case.name; binding=$binding; records=$records.ToArray(); faults=@($faults | ForEach-Object { Describe-Record $_ });
                        errors=@($Error | ForEach-Object { Describe-Record $_ }) } | ConvertTo-Json -Depth 8 -Compress
                }
            }
            $pipelines=@(
                @{name='none';value=@()},
                @{name='one';value=@(65)},
                @{name='many';value=@(65,0)},
                @{name='arrays';value=@([byte[]]@(65,0),[byte[]]@(66,0))},
                @{name='failure-between';value=@([byte[]]@(65,0),'bad',[byte[]]@(66,0))})
            foreach ($pipeline in $pipelines) {
                $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                try { $pipeline.value | Convert-BinaryToString -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                catch { [void]$records.Add((Describe-Record $_)) }
                [pscustomobject]@{ case=$pipeline.name; binding='pipeline'; records=$records.ToArray(); faults=@($faults | ForEach-Object { Describe-Record $_ });
                    errors=@($Error | ForEach-Object { Describe-Record $_ }) } | ConvertTo-Json -Depth 8 -Compress
            }
            foreach ($failure in 'none','move','current','dispose') {
                $inputValue=[ConversionInput]::new(); $inputValue.Failure=$failure; [ConversionInput]::Trace=''
                $faults=@(); $Error.Clear(); $records=[Collections.Generic.List[object]]::new()
                try { Convert-BinaryToString -Binary $inputValue -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                catch { [void]$records.Add((Describe-Record $_)) }
                [pscustomobject]@{ failure=$failure; trace=[ConversionInput]::Trace; records=$records.ToArray();
                    faults=@($faults | ForEach-Object { Describe-Record $_ }); errors=@($Error | ForEach-Object { Describe-Record $_ }) } | ConvertTo-Json -Depth 8 -Compress
            }
            'later:' + (Convert-BinaryToString -Bin ([byte[]]@(90,0)))
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-complete-conversion");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-complete-conversion");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("later:Z", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    private static string FindCompleteConversionWorkflow(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName, "Benchmarks", "PowerShellCompilation", "Corpus", "ExternalWorkflows" }.Concat(parts).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("The pinned complete workflow fixture could not be found.");
    }
}
