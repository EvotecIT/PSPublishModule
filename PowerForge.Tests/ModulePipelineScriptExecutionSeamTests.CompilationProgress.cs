namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(ConfigurationGateMode.Build, false)]
    [InlineData(ConfigurationGateMode.Build, true)]
    [InlineData(ConfigurationGateMode.Documentation, false)]
    [InlineData(ConfigurationGateMode.Documentation, true)]
    public void Run_CompilationReportsActiveAndTerminalStatesInEveryGate(ConfigurationGateMode mode, bool fail)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0", ["Get-UnsupportedValue"]);
            File.WriteAllText(Path.Combine(root.FullName, moduleName + ".psm1"),
                "function Get-UnsupportedValue { " + (fail ? "Get-Date" : "return 42") + " }; Export-ModuleMember -Function Get-UnsupportedValue");
            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(), new FakeHostedOperations());
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName, SourcePath = root.FullName, Version = "1.0.0",
                    PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                    {
                        Enabled = true, Mode = PowerShellCompilationMode.Strict, AllowUnreviewedDependencies = true
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationGateSegment { Configuration = new GateConfiguration { Mode = mode } },
                    new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { Merge = false } },
                    new ConfigurationDocumentationSegment
                    {
                        Configuration = new DocumentationConfiguration { Path = "Docs", PathReadme = "Docs/Readme.md" }
                    },
                    new ConfigurationBuildDocumentationSegment
                    {
                        Configuration = new BuildDocumentationConfiguration { Enable = true, GenerateExternalHelp = false }
                    }
                ]
            };
            var progress = new CompilationProgress();
            if (fail)
            {
                var error = Assert.ThrowsAny<Exception>(() => runner.Run(spec, runner.Plan(spec), progress));
                Assert.Equal(new[] { "started", "failed" }, progress.Events);
                Assert.Same(error, progress.Error);
            }
            else
            {
                runner.Run(spec, runner.Plan(spec), progress);
                Assert.Equal(new[] { "started", "completed" }, progress.Events);
                Assert.Null(progress.Error);
            }
        }
        finally { try { root.Delete(recursive: true); } catch { /* best effort */ } }
    }

    private sealed class CompilationProgress : IModulePipelineProgressReporter
    {
        internal List<string> Events { get; } = new();
        internal Exception? Error { get; private set; }
        public void StepStarting(ModulePipelineStep step) { if (step.Kind == ModulePipelineStepKind.Compilation) Events.Add("started"); }
        public void StepCompleted(ModulePipelineStep step) { if (step.Kind == ModulePipelineStepKind.Compilation) Events.Add("completed"); }
        public void StepFailed(ModulePipelineStep step, Exception error)
        {
            if (step.Kind != ModulePipelineStepKind.Compilation) return;
            Events.Add("failed");
            Error = error;
        }
    }
}
