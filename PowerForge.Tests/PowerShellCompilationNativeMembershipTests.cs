namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedMembership_PreservesObjectCollectionsAndEnumerationFailures(string framework, string host)
    {
        var operators = new[] { "contains", "icontains", "ccontains", "notcontains", "inotcontains", "cnotcontains",
            "in", "iin", "cin", "notin", "inotin", "cnotin" };
        var source = string.Join(Environment.NewLine, operators.Select((operation, index) => {
            var expression = operation.EndsWith("in", StringComparison.Ordinal)
                ? "$Candidate -" + operation + " $Collection" : "$Collection -" + operation + " $Candidate";
            return "function Test-ObjectMembership" + index + " { [CmdletBinding()] param([object]$Collection,[object]$Candidate) " +
                "'before'; [bool]$result=$false; $result=" + expression + "; 'after'; $result }";
        }));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class FailingMembershipSequence : IEnumerable {
                public IEnumerator GetEnumerator() { throw new InvalidOperationException("get-fault"); }
            }
            '@
            $pairs=@(
                @{collection=$null;candidate=$null},
                @{collection=@();candidate=$null},
                @{collection='Alpha';candidate='alpha'},
                @{collection='I';candidate='i'},
                @{collection=1;candidate='1'},
                @{collection=@(1,2,3);candidate='2'},
                @{collection=@('1','2');candidate=2},
                @{collection=@($null,'x');candidate=$null},
                @{collection=@((1,2),(3,4));candidate=@(1,2)},
                @{collection=[int[,]]::new(2,2);candidate=0},
                @{collection=[FailingMembershipSequence]::new();candidate=0})
            foreach($culture in 'en-US','tr-TR') {
                [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach($index in 0..11) {
                    foreach($pair in $pairs) {
                        foreach($action in 'Continue','SilentlyContinue','Stop') {
                            $records=@(try { & ('Test-ObjectMembership'+$index) -Collection $pair.collection -Candidate $pair.candidate -ErrorAction $action 2>&1 } catch { 'caught' })
                            $normalized=@(foreach($record in $records) {
                                if($record -is [Management.Automation.ErrorRecord]) {
                                    [pscustomobject]@{id=$record.FullyQualifiedErrorId;category=$record.CategoryInfo.Category.ToString()}
                                } else { $record }
                            })
                            ConvertTo-Json -InputObject $normalized -Depth 8 -Compress
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-object-membership-original");
        Assert.Equal(0, original.ExitCode);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedObjectMembership", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(operators.Length, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-object-membership-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedMembership_EvaluatesTheCollectionBeforeTheCandidate(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-InOrder { [CmdletBinding()] param([Text.StringBuilder]$Trace) return $Trace.Append('candidate;').ToString() -in ([string[]]($Trace.Append('collection;').ToString())) }
            function Test-ContainsOrder { [CmdletBinding()] param([Text.StringBuilder]$Trace) return ([string[]]($Trace.Append('collection;').ToString())) -contains $Trace.Append('candidate;').ToString() }
            function Get-ArrayRecords { [CmdletBinding()] param() return @(1,2) }
            function Get-ArrayVector { [CmdletBinding()] param([int[]]$Values) return @($Values) }
            function Get-NestedRecords { [CmdletBinding()] param() return @(,(1,2)) }
            function Get-MultipleRecords { [CmdletBinding()] param() return @(1,2;3,4) }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedMembershipOrder", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(6, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        const string probe = """
            foreach($name in 'Test-InOrder','Test-ContainsOrder') {
                $trace=[Text.StringBuilder]::new()
                $value=& $name -Trace $trace
                [pscustomobject]@{name=$name;value=$value;trace=$trace.ToString()} | ConvertTo-Json -Compress
            }
            foreach($name in 'Get-ArrayRecords','Get-NestedRecords','Get-MultipleRecords') {
                $records=@(& $name)
                [pscustomobject]@{name=$name;records=$records;types=@($records | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Depth 8 -Compress
            }
            $records=@(Get-ArrayVector -Values @(1,2))
            [pscustomobject]@{name='Get-ArrayVector';records=$records;types=@($records | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Depth 8 -Compress
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-membership-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-membership-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("collection;candidate;", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("[1,2]", original.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeMembership_PreservesComparisonOrderEnumerationAndErrors(string framework, string host)
    {
        var operators = new[] { "contains", "icontains", "ccontains", "notcontains", "inotcontains", "cnotcontains",
            "in", "iin", "cin", "notin", "inotin", "cnotin" };
        var source = string.Join(Environment.NewLine, operators.Select((operation, index) => {
            var collection = "$Probe.ReadCollection($Collection)";
            var candidate = "$Probe.ReadCandidate($Candidate)";
            var expression = operation.EndsWith("in", StringComparison.Ordinal)
                ? candidate + " -" + operation + " " + collection : collection + " -" + operation + " " + candidate;
            return "function Test-Membership" + index + " { [CmdletBinding()] param([object]$Collection,[object]$Candidate,[object]$Probe) " +
                "'before'; $result='prior'; $result=" + expression + "; $success=$?; 'after'; $result; $success }";
        }));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeMembership", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries.Where(unit => unit.Name.StartsWith("Test-Membership", StringComparison.Ordinal)).ToArray();
        Assert.Equal(operators.Length, units.Length);
        Assert.All(units, unit => {
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        var probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Collections.Generic;
            public sealed class MembershipProbe {
                public static readonly List<string> Events=new List<string>();
                public string Failure;
                public object ReadCollection(object value) { Events.Add("collection"); if(Failure=="collection")throw new InvalidOperationException("collection-fault"); return value; }
                public object ReadCandidate(object value) { Events.Add("candidate"); if(Failure=="candidate")throw new InvalidOperationException("candidate-fault"); return value; }
            }
            public sealed class MembershipSequence : IEnumerable, IEnumerator, IDisposable {
                public string Failure; private int index=-1;
                public IEnumerator GetEnumerator() { MembershipProbe.Events.Add("get"); if(Failure=="get")throw new InvalidOperationException("get-fault"); return this; }
                public bool MoveNext() { index++;MembershipProbe.Events.Add("move:"+index);if(index==1&&Failure=="move")throw new InvalidOperationException("move-fault");return index<3; }
                public object Current { get { MembershipProbe.Events.Add("current:"+index);if(index==1&&Failure=="current")throw new InvalidOperationException("current-fault");return index; } }
                public void Reset() { throw new NotSupportedException(); }
                public void Dispose() { MembershipProbe.Events.Add("dispose"); }
            }
            public sealed class MembershipElement {
                public bool Fail;
                public override bool Equals(object other) { MembershipProbe.Events.Add("equals:"+(other==null?"null":other.GetType().Name));if(Fail)throw new InvalidOperationException("equals-fault");return object.Equals(other,"match"); }
                public override int GetHashCode() { return 0; }
            }
            '@
            function Describe-MembershipValue($item) {
                if($null -eq $item) { return [pscustomobject]@{kind='null'} }
                if($item -is [Management.Automation.ErrorRecord]) {
                    return [pscustomobject]@{kind='error';id=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        message=$item.Exception.Message;category=[string]$item.CategoryInfo.Category;
                        line=$item.InvocationInfo.ScriptLineNumber;column=$item.InvocationInfo.OffsetInLine}
                }
                if($item -is [Exception]) { return [pscustomobject]@{kind='exception';type=$item.GetType().FullName;message=$item.Message} }
                [pscustomobject]@{kind=$item.GetType().FullName;value=$item}
            }
            $factories=@(
                {@{collection=$null;candidate=$null}},
                {@{collection=@();candidate=$null}},
                {@{collection='Alpha';candidate='alpha'}},
                {@{collection='I';candidate='i'}},
                {@{collection=1;candidate='1'}},
                {@{collection=@(1,2,3);candidate='2'}},
                {@{collection=@('1','2');candidate=2}},
                {@{collection=@([uint32]::MaxValue,[long]::MaxValue);candidate=[decimal][long]::MaxValue}},
                {@{collection=@($null,'x');candidate=$null}},
                {@{collection=@((1,2),(3,4));candidate=@(1,2)}},
                {@{collection=[int[,]]::new(2,2);candidate=0}},
                {$value=@{name=1};@{collection=$value;candidate=$value}},
                {@{collection=[MembershipSequence]@{Failure=''};candidate=0}},
                {@{collection=[MembershipSequence]@{Failure=''};candidate=2}},
                {@{collection=[MembershipSequence]@{Failure='get'};candidate=9}},
                {@{collection=[MembershipSequence]@{Failure='move'};candidate=9}},
                {@{collection=[MembershipSequence]@{Failure='current'};candidate=9}},
                {@{collection=@([MembershipElement]@{Fail=$false});candidate='match'}},
                {@{collection=@([MembershipElement]@{Fail=$true});candidate='match'}},
                {@{collection=@(1,2);candidate=1;failure='collection'}},
                {@{collection=@(1,2);candidate=1;failure='candidate'}})
            foreach($culture in 'en-US','tr-TR') {
                [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                for($function=0;$function -lt FUNCTION_COUNT;$function++) {
                    for($case=0;$case -lt $factories.Count;$case++) {
                        foreach($action in 'Continue','SilentlyContinue','Stop') {
                            $pair=& $factories[$case];$observer=[MembershipProbe]@{Failure=$pair.failure}
                            [MembershipProbe]::Events.Clear();$Error.Clear();$faults=@();$records=[Collections.Generic.List[object]]::new()
                            try {& ('Test-Membership'+$function) -Collection $pair.collection -Candidate $pair.candidate -Probe $observer -ErrorAction $action -ErrorVariable faults 2>&1 |
                                ForEach-Object {$records.Add((Describe-MembershipValue $_))}}
                            catch {$records.Add((Describe-MembershipValue $_))}
                            [pscustomobject]@{culture=$culture;function=$function;case=$case;action=$action;records=$records.ToArray();
                                events=[MembershipProbe]::Events.ToArray();faults=@($faults|ForEach-Object {Describe-MembershipValue $_});
                                errors=@($Error|ForEach-Object {Describe-MembershipValue $_})} | ConvertTo-Json -Depth 8 -Compress
                        }
                    }
                }
            }
            @(Test-Membership0 -Collection @(1,2) -Candidate 2 -Probe ([MembershipProbe]::new()) | Select-Object -First 1) | ConvertTo-Json -Compress
            @(Test-Membership0 -Collection @(1,2) -Candidate 2 -Probe ([MembershipProbe]::new())) | ConvertTo-Json -Compress
            """.Replace("FUNCTION_COUNT", operators.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "membership-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "membership-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.Length >= operators.Length * 21 * 3 * 2 + 2);
        var differences = expected.Select((line, index) => (line, actual: actual[index], index)).Where(item => item.line != item.actual).ToArray();
        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.GroupBy(item => item.index / 63).Select(group => group.First()).Take(6)
            .Select(item => $"Record {item.index}: original={item.line}{Environment.NewLine}generated={item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
