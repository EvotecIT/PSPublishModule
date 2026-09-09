using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedTimeSpanPreservesTypedCollectionOutput(string framework, string host)
        => VerifyPinnedTimeSpanWorkflow(framework, host, nativeCommand: false);

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedTimeSpanPreservesNativeLifecycleOutput(string framework, string host)
        => VerifyPinnedTimeSpanWorkflow(framework, host, nativeCommand: true);

    private void VerifyPinnedTimeSpanWorkflow(string framework, string host, bool nativeCommand)
    {
        var source = FindCompleteConversionWorkflow("SamErde", "Format-Timespan.ps1");
        Assert.Equal("2f502f153597259dda9e9520685ce31ed2a5d8c7981099c4d41b2229ff3ae028",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(string.Empty, ".psm1");
        File.Copy(source, fixture.ScriptPath, overwrite: true);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompleteTimeSpan",
            nativeCommand ? PowerShellCompilationArtifactKind.BinaryModule : PowerShellCompilationArtifactKind.Library,
            nativeCommand ? PowerShellCompilationMode.Hybrid : PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        if (!nativeCommand)
        {
            Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
            Assert.False(result.Manifest.RequiresPowerShellRuntime);
            Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
            Assert.False(result.Manifest.AllowsPowerShellRuntimeEvaluation);
            var disposition = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, static entry => entry.Name == "Format-TimeSpan");
            Assert.True(disposition.EmittedClrMethod);
            Assert.False(disposition.RetainedHostedSource);
            Assert.Equal(0, disposition.RuntimeCommandRegions);
            var methodAbi = Assert.Single(result.Manifest.PublicAbi!.Methods);
            Assert.Equal(typeof(string[]).FullName, methodAbi.ReturnType);
            Assert.Equal(typeof(TimeSpan[]).FullName, methodAbi.Parameters[0].TypeName);
            Assert.True(methodAbi.Parameters[0].Required);
            Assert.False(methodAbi.Parameters[0].Nullable);
            Assert.True(methodAbi.Parameters[0].AllowEmptyCollection);
            Assert.True(methodAbi.Parameters[1].IsSwitch);
            Assert.Equal("ClrDirect", methodAbi.ExceptionContract);
        }
        else
        {
            var disposition = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, static entry => entry.Name == "Format-TimeSpan");
            Assert.True(disposition.EmittedClrMethod);
            Assert.False(disposition.RetainedHostedSource);
            Assert.Equal(PowerShellCompilationLifecycleExecution.CompiledNativeCallbacks, Assert.Single(result.Manifest.Lifecycles).Execution);
        }
        const string probe = """
            $cases=[Collections.Generic.List[object]]::new()
            foreach($ticks in 0L,1L,-1L,9999L,10000L,9999999L,10000000L,599999999L,600000000L,600000001L,35999999999L,36000000000L,36610000000L,864000000000L,900610000000L,[long]::MaxValue,[long]::MinValue,-10000L,-10000000L,-600000000L) {
                $cases.Add(@{name=$ticks.ToString();values=[TimeSpan[]]@([TimeSpan]::FromTicks($ticks))})
            }
            $cases.Add(@{name='empty';values=[TimeSpan[]]@()})
            $cases.Add(@{name='many';values=[TimeSpan[]]@([TimeSpan]::Zero,[TimeSpan]::FromTicks(1),[TimeSpan]::FromSeconds(61),[TimeSpan]::MaxValue,[TimeSpan]::MinValue)})
            $cases.Add(@{name='repeated';values=[TimeSpan[]]@([TimeSpan]::FromTicks(1),[TimeSpan]::Zero,[TimeSpan]::FromTicks(1))})
            foreach($culture in 'en-US','pl-PL','de-DE') {
                [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach($binding in 'pipeline','named','positional') { foreach($abbreviate in $false,$true) {
                    foreach($case in $cases) {
                        $values=@(Invoke-Case $case.values $abbreviate $binding)
                        [pscustomobject]@{culture=$culture;binding=$binding;abbreviate=$abbreviate;case=$case.name;count=$values.Count;
                            types=@($values | ForEach-Object {$_.GetType().FullName});
                            values=@($values | ForEach-Object {[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_))})} | ConvertTo-Json -Compress
                    }
                } }
            }
            """;
        var commandProbe =
            "function Invoke-Case($values,$abbreviate,$binding){ " +
            "if($binding -eq 'pipeline'){$values | Format-TimeSpan -Abbreviate:$abbreviate} " +
            "elseif($binding -eq 'named'){foreach($value in $values){Format-TimeSpan -TimeSpan $value -Abbreviate:$abbreviate}} " +
            "else{foreach($value in $values){Format-TimeSpan $value -Abbreviate:$abbreviate}} }; " + probe;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + commandProbe,
            fixture.RootPath, "original-complete-timespan");
        var compiled = RunStatementErrorProbe(host, nativeCommand
            ? "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + commandProbe
            : "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) + "'); " +
            "$method=($assembly.GetTypes() | Where-Object {$null -ne $_.GetMethod('Format_TimeSpan')}).GetMethod('Format_TimeSpan'); " +
            "$rejected=$false;try{[void]$method.Invoke($null,[object[]]@($null,$false))}catch{$failure=$_.Exception;while($null -ne $failure.InnerException){$failure=$failure.InnerException};" +
            "$rejected=$failure.GetType().FullName -eq 'System.ArgumentException'};if(-not $rejected){throw 'Expected the declared non-null CLR collection contract'}; " +
            "function Invoke-Case($values,$abbreviate,$binding){$method.Invoke($null,[object[]]@($values,[bool]$abbreviate))}; " + probe,
            fixture.RootPath, "compiled-complete-timespan");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Empty(compiled.StandardError);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(414, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
        if (nativeCommand) VerifyTimeSpanCommandFailures(fixture, result.ArtifactPath!, host);
        else if (framework == "net10.0") VerifyTimeSpanFromIndependentConsumer(fixture.RootPath, result);
    }

    private void VerifyTimeSpanCommandFailures(ArtifactFixture fixture, string artifact, string host)
    {
        const string probe = """
            function Describe-TimeSpanError($value) {
                $record=if($value -is [Management.Automation.ErrorRecord]) {$value} else {$value.ErrorRecord}
                if($null -eq $record) {return $value.GetType().FullName}
                [pscustomobject]@{id=$record.FullyQualifiedErrorId;type=$record.Exception.GetType().FullName;category=[string]$record.CategoryInfo.Category}
            }
            foreach($shape in 'null','invalid','overflow','nested','empty','mixed') {
                foreach($action in 'Continue','SilentlyContinue','Stop') {
                    foreach($stop in $false,$true) {
                        $values=switch($shape) {
                            'null' {,@($null,[TimeSpan]::FromSeconds(1))}
                            'invalid' {,@('invalid',[TimeSpan]::Zero)}
                            'overflow' {,@('10675199.02:48:05.4775808',[TimeSpan]::Zero)}
                            'nested' {,@(@([TimeSpan]::Zero,[TimeSpan]::FromSeconds(1)),[TimeSpan]::Zero)}
                            'empty' {,@()}
                            'mixed' {,@([TimeSpan]::Zero,'invalid',[TimeSpan]::FromSeconds(1))}
                        }
                        $errors=@();$caught=$null;$records=[Collections.Generic.List[string]]::new()
                        try {
                            if($stop){$values | Format-TimeSpan -Abbreviate -ErrorAction $action -ErrorVariable errors 2>$null | Select-Object -First 1 | ForEach-Object {$records.Add($_)}}
                            else {$values | Format-TimeSpan -Abbreviate -ErrorAction $action -ErrorVariable errors 2>$null | ForEach-Object {$records.Add($_)}}
                        } catch {$caught=Describe-TimeSpanError $_}
                        $later=@([TimeSpan]::FromSeconds(2) | Format-TimeSpan -Abbreviate)
                        [pscustomobject]@{shape=$shape;action=$action;stop=$stop;records=@($records);errors=@($errors | ForEach-Object {Describe-TimeSpanError $_});caught=$caught;later=$later} | ConvertTo-Json -Compress -Depth 8
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-timespan-failures");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(artifact) + "'; " + probe,
            fixture.RootPath, "compiled-timespan-failures");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(36, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    private static void VerifyTimeSpanFromIndependentConsumer(string root, PowerShellCompilationBuildResult result)
    {
        var directory = Path.Combine(root, "timespan-consumer");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "TimeSpanConsumer.csproj");
        var project = new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                new System.Xml.Linq.XElement("OutputType", "Exe"),
                new System.Xml.Linq.XElement("ImplicitUsings", "enable")),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference",
                new System.Xml.Linq.XAttribute("Include", "GeneratedCompleteTimeSpan"),
                new System.Xml.Linq.XElement("HintPath", result.ArtifactPath!))));
        new System.Xml.Linq.XDocument(project).Save(projectPath);
        var abi = result.Manifest!.PublicAbi!;
        File.WriteAllText(Path.Combine(directory, "Program.cs"),
            "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var values = Methods.Format_TimeSpan(new[] { TimeSpan.Zero, TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(1500), TimeSpan.FromDays(1) }, true);
            if (!values.SequenceEqual(new[] { "0s", "0.10\u00b5s", "1.50s", "1d" })) return 1;
            if (Methods.Format_TimeSpan(Array.Empty<TimeSpan>(), false).Length != 0) return 2;
            try { Methods.Format_TimeSpan(null!, false); return 3; } catch (ArgumentException) { }
            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "System.Management.Automation")) return 4;
            Console.WriteLine("runtime-free complete TimeSpan workflow passed");
            return 0;
            """);
        var build = RunProcess("dotnet", "build", projectPath, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var run = RunProcess("dotnet", Path.Combine(directory, "bin", "Release", "net10.0", "TimeSpanConsumer.dll"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Contains("runtime-free complete TimeSpan workflow passed", run.StandardOutput, StringComparison.Ordinal);
    }
}
