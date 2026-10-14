using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_NativeStatementValuesPreserveArrayRecordsAcrossHelpersAndRoot()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string]$Mode='none',[Alias('b')][switch]$Bare)
            function Read-Returned {
                param([object[]]$Items,[bool]$Bare)
                if($Bare){return}
                return @(foreach($item in $Items){if($item){[string]$item}})
            }
            function Read-Implicit {
                param([object[]]$Items,[bool]$Bare)
                if($Bare){return}
                @(foreach($item in $Items){if($item){[string]$item}})
            }
            $items=switch($Mode){'one'{'one'};'many'{'one';'two';'three'}}
            $returned=@(Read-Returned -Items $items -Bare:$Bare)
            $implicit=@(Read-Implicit -Items $items -Bare:$Bare)
            $record=[pscustomobject]@{Values=@(foreach($item in $items){if($item){[string]$item}})}
            [pscustomobject]@{
                ReturnedCount=$returned.Count;ReturnedType=$returned.GetType().FullName;Returned=$returned
                ImplicitCount=$implicit.Count;ImplicitType=$implicit.GetType().FullName;Implicit=$implicit
                RootCount=$record.Values.Count;RootType=$record.Values.GetType().FullName;Root=$record.Values
            }|ConvertTo-Json -Compress -Depth 5
            """, compactPath: true);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Generated.NativeStatementValues", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            SingleFile = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var built = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var root = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Kind == PowerShellCompilationUnitKind.Script);
        Assert.True(root.Emitted);
        Assert.True(root.UsesNativeFunctionBinding);
        Assert.False(root.RetainedHostedSource);
        foreach (var name in new[] { "Read-Returned", "Read-Implicit" })
        {
            var helper = Assert.Single(built.Manifest.UnitDispositionLedger.Entries, entry => entry.Name == name);
            Assert.True(helper.EmittedClrMethod);
            Assert.True(helper.UsesNativeFunctionBinding);
            Assert.False(helper.RetainedHostedSource);
        }
        var host = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH") ?? "pwsh";
        foreach (var mode in new[] { "none", "one", "many" })
        {
            foreach (var bare in new[] { false, true })
            {
                var arguments = bare ? new[] { "-Mode", mode, "-Bare" } : new[] { "-Mode", mode };
                var original = RunProcess(host, new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }.Concat(arguments).ToArray());
                var generated = RunProcess(built.ArtifactPath!, arguments);
                Assert.Equal(0, original.ExitCode);
                Assert.Empty(original.StandardError);
                Assert.Equal(original.ExitCode, generated.ExitCode);
                Assert.True(original.StandardOutput == generated.StandardOutput,
                    $"Mode={mode}; Bare={bare}; Original: {original.StandardOutput}; Generated: {generated.StandardOutput}");
                Assert.Equal(original.StandardError, generated.StandardError);
                using var observation = System.Text.Json.JsonDocument.Parse(generated.StandardOutput);
                var expectedCount = mode == "none" ? 0 : mode == "one" ? 1 : 3;
                foreach (var prefix in new[] { "Returned", "Implicit", "Root" })
                {
                    var count = prefix != "Root" && bare ? 0 : expectedCount;
                    Assert.Equal(count, observation.RootElement.GetProperty(prefix + "Count").GetInt32());
                    Assert.Equal("System.Object[]", observation.RootElement.GetProperty(prefix + "Type").GetString());
                    Assert.Equal(count, observation.RootElement.GetProperty(prefix).GetArrayLength());
                }
            }
        }
        foreach (var binding in new[]
        {
            ("alias-presence", new[] { "-Mode", "one", "-b" }, new[] { "-Mode", "one", "-b" }, 0),
            ("inline-true", new[] { "-Mode", "one", "-Bare:$true" }, new[] { "-Mode", "one", "-Bare=true" }, 0),
            ("inline-false", new[] { "-Mode", "one", "-Bare:$false" }, new[] { "-Mode", "one", "-Bare=false" }, 1),
            ("positional", new[] { "one" }, new[] { "one" }, 1)
        })
        {
            var original = RunProcess(host, new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }.Concat(binding.Item2).ToArray());
            var generated = RunProcess(built.ArtifactPath!, binding.Item3);
            Assert.Equal(0, original.ExitCode);
            Assert.Empty(original.StandardError);
            Assert.Equal(original.ExitCode, generated.ExitCode);
            Assert.True(original.StandardOutput == generated.StandardOutput,
                $"Binding={binding.Item1}; Original: {original.StandardOutput}; Generated: {generated.StandardOutput}");
            Assert.Equal(original.StandardError, generated.StandardError);
            using var observation = System.Text.Json.JsonDocument.Parse(generated.StandardOutput);
            Assert.Equal(binding.Item4, observation.RootElement.GetProperty("ReturnedCount").GetInt32());
            Assert.Equal(binding.Item4, observation.RootElement.GetProperty("ImplicitCount").GetInt32());
            Assert.Equal(1, observation.RootElement.GetProperty("RootCount").GetInt32());
        }
    }
}
