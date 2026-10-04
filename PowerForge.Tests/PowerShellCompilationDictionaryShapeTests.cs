using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void DictionaryShapes_PreserveMutationsThroughBranchesLoopsAndAliases(string framework, string host)
    {
        var cases = new (string Name, string Body, string Key)[] {
            ("Direct", "$h=TABLE; $h.Add('b',1)", "b"),
            ("Branch", "if($Flag) { $h=TABLE } else { $h=TABLE }; $h.Add('b',1)", "b"),
            ("Alias", "$h=TABLE; $alias=$h; $alias.Add('b',1)", "b"),
            ("Interface", "$h=TABLE; [Collections.IDictionary]$alias=$h; $alias.Add('b',1)", "b"),
            ("Loop", "$h=TABLE; for([int]$i=0;$i -lt 1;$i++) { $h.Add('b',1) }", "b"),
            ("Index", "$h=TABLE; if($Flag) { $h['b']=1 } else { $h['b']=2 }", "b"),
            ("AliasIndex", "$h=TABLE; $alias=$h; $alias['b']=1", "b"),
            ("Member", "$h=TABLE; $h.a=1", "a"),
            ("CountMember", "$h=TABLE; $h.Count=1", "Count"),
            ("ForeachAlias", "$h=TABLE; foreach($item in $h) { $alias=[Collections.IDictionary]$item; $alias.Add('b',1) }", "b"),
            ("StoredAlias", "$h=TABLE; $cells=[Collections.ArrayList]::new(); $null=$cells.Add($h); $entry=$cells[0]; $alias=[Collections.IDictionary]$entry; $alias.Add('b',1)", "b"),
        };
        var names = new List<string>();
        var functions = new List<string>();
        foreach(var ordered in new[] { false, true })
            foreach(var item in cases)
            {
                var name = (ordered ? "Ordered" : "Hashtable") + item.Name;
                names.Add(name);
                functions.Add("function Read-" + name + " { [CmdletBinding()] param([bool]$Flag,[Collections.Generic.List[object]]$Observed) " +
                    item.Body.Replace("TABLE", (ordered ? "[ordered]" : "") + "@{a='x'; Count='old'}") +
                    "; $value=$h['" + item.Key + "']; $Observed.Add($value); return 1 }");
            }
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, functions), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DictionaryMutationShapes", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(names.Count, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        var probe = "$names=@('" + string.Join("','", names) + "'); " + """
            foreach($name in $names) {
                foreach($flag in $true,$false) {
                    $observed=[Collections.Generic.List[object]]::new()
                    $records=@(& ('Read-'+$name) -Flag $flag -Observed $observed -ErrorAction Stop)
                    [pscustomobject]@{name=$name;flag=$flag;records=$records;values=$observed.ToArray();types=@($observed | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-dictionary-shapes");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-dictionary-shapes");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(false, "Direct")]
    [InlineData(true, "Direct")]
    [InlineData(false, "Alias")]
    [InlineData(true, "Alias")]
    [InlineData(false, "Interface")]
    [InlineData(true, "Interface")]
    [InlineData(false, "Branch")]
    [InlineData(true, "Branch")]
    [InlineData(false, "Add")]
    [InlineData(true, "Add")]
    [InlineData(false, "Index")]
    [InlineData(true, "Index")]
    public void DictionaryShapes_PreserveClosedStringLookupPrecision(bool ordered, string scenario)
    {
        var table = (ordered ? "[ordered]" : "") + "@{a='x'}";
        var body = scenario switch {
            "Alias" => "$h=" + table + "; $alias=$h; $v=$alias['a']",
            "Interface" => "$h=" + table + "; [Collections.IDictionary]$alias=$h; $v=$alias['a']",
            "Branch" => "if($Flag) { $h=" + table + " } else { $h=" + table + " }; $v=$h['a']",
            "Add" => "$h=" + table + "; $h.Add('b',$Text); $v=$h['b']",
            "Index" => "$h=" + table + "; $h['a']=$Text; $v=$h['a']",
            _ => "$h=" + table + "; $v=$h['a']"
        };
        using var fixture = ArtifactFixture.Create(
            "function Test-MapValue { [CmdletBinding()] param([bool]$Flag,[string]$Text) " + body + "; return [string]::IsNullOrEmpty($v) }", ".psm1");
        var library = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Single(library.Methods);
        var command = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "DictionaryPrecision", "net10.0");
        Assert.Single(command.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void DictionaryShapes_CommandHostPreservesOpenBranchMutationOutput(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-MapValue {
                [CmdletBinding()] param([bool]$Flag)
                if($Flag) { $h=@{a='x'} } else { $h=@{a='y'} }
                $h.Add('b',1)
                return $h['b']
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DictionaryBranchValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach($flag in $true,$false) {
                try {
                    $values=@(Get-MapValue -Flag $flag -ErrorAction Stop)
                    [pscustomobject]@{flag=$flag;values=$values;types=@($values | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Compress
                } catch {
                    [pscustomobject]@{flag=$flag;error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-dictionary-branch");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-dictionary-branch");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
