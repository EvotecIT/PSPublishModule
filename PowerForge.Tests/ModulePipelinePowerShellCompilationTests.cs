using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ModulePipelinePowerShellCompilationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_compiles_an_arbitrary_script_module_and_preserves_source_authoring_shape(bool documentationGate)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(testRoot, "source");
        var stagingRoot = Path.Combine(testRoot, "staging");
        const string moduleName = "Generic.Random.Module";
        Directory.CreateDirectory(sourceRoot);
        var sourceManifest = Path.Combine(sourceRoot, moduleName + ".psd1");
        File.WriteAllText(Path.Combine(sourceRoot, moduleName + ".psm1"),
            """
            function Get-AdjustedLimit {
                [CmdletBinding()]
                param([double] $Baseline, [double] $Ratio, [double] $Offset)
                $relative = $Baseline * (1.0 + $Ratio)
                $absolute = $Baseline + $Offset
                if ($relative -gt $absolute) { return $relative }
                return $absolute
            }
            Export-ModuleMember -Function Get-AdjustedLimit
            """);
        File.WriteAllText(sourceManifest,
            """
            @{
                RootModule = 'Generic.Random.Module.psm1'
                ModuleVersion = '1.0.0'
                GUID = '15d46115-ed16-4266-bc16-d56255761a40'
                FunctionsToExport = @('Get-AdjustedLimit')
                CmdletsToExport = @()
                AliasesToExport = @()
            }
            """);
        File.WriteAllText(Path.Combine(sourceRoot, "README.md"), "generic module payload");
        File.WriteAllText(Path.Combine(sourceRoot, "excluded.txt"), "must not ship");

        try
        {
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                        {
                            Enabled = true,
                            Mode = PowerShellCompilationMode.Hybrid,
                            TargetFramework = "net8.0",
                            IncludeResource = new[] { "README.md" },
                            ExcludeResource = new[] { "excluded.txt" },
                            AllowUnreviewedDependencies = true,
                            UseBuildCache = true
                        }
                    }
                }
            };
            if (documentationGate)
            {
                segments.Add(new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration
                    {
                        Mode = ConfigurationGateMode.Documentation
                    }
                });
                segments.Add(new ConfigurationDocumentationSegment
                {
                    Configuration = new DocumentationConfiguration
                    {
                        Path = "Docs",
                        PathReadme = "README.md"
                    }
                });
                segments.Add(new ConfigurationBuildDocumentationSegment
                {
                    Configuration = new BuildDocumentationConfiguration
                    {
                        Enable = true,
                        GenerateExternalHelp = false,
                        SyncExternalHelpToProjectRoot = false
                    }
                });
            }

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = sourceRoot,
                    StagingPath = stagingRoot,
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };

            var result = new ModulePipelineRunner(new NullLogger()).Run(spec);

            var compilation = Assert.IsType<PowerShellModuleCompilationResult>(result.PowerShellCompilationResult);
            Assert.True(compilation.CompiledUnits > 0);
            Assert.True(compilation.CoveragePercentage > 0);
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".dll")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".psd1")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, "README.md")));
            Assert.False(File.Exists(Path.Combine(stagingRoot, "excluded.txt")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".powerforge-module-compilation.json")));
            Assert.Contains("Generic.Random.Module.psm1", File.ReadAllText(sourceManifest), StringComparison.Ordinal);

            if (!documentationGate)
            {
                var originalAssembly = File.ReadAllBytes(Path.Combine(stagingRoot, moduleName + ".dll"));
                spec.Build.ReuseStaging = true;
                spec.Build.SkipDotNetBuild = true;
                var reused = new ModulePipelineRunner(new NullLogger()).Run(spec);
                var reusedCompilation = Assert.IsType<PowerShellModuleCompilationResult>(reused.PowerShellCompilationResult);
                Assert.Equal(compilation.CompiledUnits, reusedCompilation.CompiledUnits);
                Assert.Equal(originalAssembly, File.ReadAllBytes(Path.Combine(stagingRoot, moduleName + ".dll")));
                Assert.Contains(moduleName + ".dll", File.ReadAllText(Path.Combine(stagingRoot, moduleName + ".psd1")), StringComparison.Ordinal);

                var compilationConfiguration = Assert.IsType<PowerShellModuleCompilationConfiguration>(
                    Assert.IsType<ConfigurationBuildSegment>(segments[0]).BuildModule.PowerShellCompilation);
                compilationConfiguration.IncludeResource = new[] { "README.md", "other.txt" };
                var contractError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", contractError.Message, StringComparison.Ordinal);
                compilationConfiguration.IncludeResource = new[] { "README.md" };

                var unexpectedPayload = Path.Combine(stagingRoot, "unexpected.txt");
                File.WriteAllText(unexpectedPayload, "not checkpointed");
                var payloadError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("payload", payloadError.Message, StringComparison.Ordinal);
                File.Delete(unexpectedPayload);
            }

            var escapedManifest = result.BuildResult.ManifestPath.Replace("'", "''", StringComparison.Ordinal);
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add($"Import-Module -Name '{escapedManifest}' -Force; Get-AdjustedLimit 100 0.2 30");
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), "Generated module invocation did not finish.");
            Assert.True(process.ExitCode == 0, error + Environment.NewLine + output);
            Assert.Contains("130", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }
}
