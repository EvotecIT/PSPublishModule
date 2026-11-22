using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task NativeScriptLifecyclePreservesPipelineBindingPartialFailureAndRecoveryOnBothHosts()
    {
        const string source = """
            [CmdletBinding()]
            param([Parameter(ValueFromPipeline=$true)][ValidateRange(0,99)][int] $Value, [switch] $Fail)
            begin { $sum = 0; 'begin'; [void] $global:LifecycleTrace.Add('begin') }
            process {
                try {
                    $sum += $Value
                    "process:$Value;cmdlet=$([bool]$PSCmdlet)"
                    if ($Fail -and $Value -eq 13) { throw 'controlled lifecycle failure' }
                } finally { [void] $global:LifecycleTrace.Add("finally:$Value") }
            }
            end { "end:$sum"; [void] $global:LifecycleTrace.Add('end') }
            """;
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost));
        var repository = PowerForgeCliTestHost.FindRepositoryRoot();
        var template = Path.Combine(repository, "PowerForge.Tests", "Fixtures",
            "PowerShellCompilationStatementErrorFixture", "PowerShellCompilationStatementErrorFixture.csproj");
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var script = Path.Combine(fixture.RootPath, "Lifecycle.ps1");
            File.WriteAllText(script, source);
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(script,
                PowerShellCompilationMode.Hybrid, targetFramework: framework,
                capabilities: PowerShellCompilationCapabilities.HybridExecutable));
            var compiled = PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(script, plan, framework, profile);
            var binding = compiled.EntryPointMethod.NativeFunctionBinding;
            Assert.NotNull(binding);
            Assert.True(binding.HasBegin && binding.HasProcess && binding.HasEnd);
            File.WriteAllText(Path.Combine(fixture.RootPath, "CompiledRoot.cs"), PowerShellHybridNativeEntryEmitter.GenerateSource(compiled, PowerShellSourceParser.ParseFile(script)));
            var project = Path.Combine(fixture.RootPath, "Lifecycle.csproj");
            File.WriteAllText(project, File.ReadAllText(template)
                .Replace("..\\..\\..\\PowerForge.Compiler", Path.Combine(repository, "PowerForge.Compiler"), StringComparison.Ordinal)
                .Replace("7.6.5", "7.6.6", StringComparison.Ordinal));
            var build = await new ProcessRunner().RunAsync(PowerShellCompilationSdkProcessEnvironment.CreateInheritedRequest(
                fixture.RootPath, new[] { "build", project, "-c", "Release", "-f", framework, "--nologo" },
                TimeSpan.FromMinutes(3), new Dictionary<string, string?>()));
            Assert.True(build.ExitCode == 0, build.StdOut + Environment.NewLine + build.StdErr);
            var assembly = Path.Combine(fixture.RootPath, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
            Assert.True(File.Exists(assembly), build.StdOut + build.StdErr);
            PowerShellCompilationSemanticOracleEnvelope Observe(bool generated)
            {
                static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
                File.WriteAllText(fixture.ScriptPath,
                    "[void][Reflection.Assembly]::LoadFrom(" + Quote(assembly) + ")\n$source=" + Quote(script) +
                    "\n$compiled=" + (generated ? "$true" : "$false") + "\n" + """
                    $rows = @(foreach ($case in @(
                        @{values=@();fail=$false}, @{values=@(2);fail=$false},
                        @{values=@(1,2,3);fail=$false}, @{values=@(1,13,4);fail=$true},
                        @{values=@(100);fail=$false}, @{values=@(4);fail=$false}
                    )) {
                        $global:LifecycleTrace=[Collections.Generic.List[string]]::new()
                        $output=[Collections.Generic.List[string]]::new()
                        $entry = if ($compiled) {
                            [PowerForge.Compiled.CompiledNativeScriptEntry]::Create($ExecutionContext.SessionState,$source)
                        } else { Get-Command $source }
                        $failure=try {
                            @($case.values) | & $entry -Fail:([bool]$case.fail) -ErrorAction Stop | ForEach-Object {$output.Add($_)}
                            $null
                        } catch { $_.FullyQualifiedErrorId }
                        [ordered]@{output=@($output.ToArray());trace=@($global:LifecycleTrace.ToArray());failure=$failure}
                    })
                    $rows | ConvertTo-Json -Depth 7 -Compress
                    """);
                return new PowerShellCompilationSemanticOracleRunner().Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                {
                    HostExecutablePath = host,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                        PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                });
            }
            var original = Observe(false);
            var generated = Observe(true);
            Assert.Empty(original.ErrorRecords);
            Assert.True(!generated.ErrorRecords.Any(), System.Text.Json.JsonSerializer.Serialize(generated.ErrorRecords));
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("process:3;cmdlet=True", value, StringComparison.Ordinal);
            Assert.Contains("end:6", value, StringComparison.Ordinal);
            Assert.Contains("finally:13", value, StringComparison.Ordinal);
            Assert.Contains("ParameterArgumentValidationError", value, StringComparison.Ordinal);
            Assert.Contains("end:4", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
