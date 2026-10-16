namespace PowerForge.Tests;

public sealed partial class ModulePipelinePowerShellCompilationTests
{
    [Fact]
    public void Public_pipeline_rejects_compilation_that_would_rename_the_published_module_before_staging()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        const string moduleName = "My Module";
        var sourceRoot = CreateDslModule(testRoot, moduleName, "function Get-Value { return 7 }");
        var staging = Path.Combine(testRoot, "staging");
        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourceRoot,
                StagingPath = staging,
                Version = "1.0.0",
                PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                {
                    Enabled = true,
                    Mode = PowerShellCompilationMode.Hybrid,
                    TargetFramework = "net10.0",
                    AllowUnreviewedDependencies = true
                }
            }
        };
        var sourceFiles = Directory.EnumerateFiles(sourceRoot).ToDictionary(
            static path => path, static path => File.ReadAllBytes(path), StringComparer.Ordinal);

        try
        {
            var runner = new ModulePipelineRunner(new NullLogger());
            var exception = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec));
            Assert.Contains("preserve its published module name", exception.Message, StringComparison.Ordinal);
            Assert.Contains("My_Module", exception.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.False(Directory.Exists(staging));
            Assert.All(sourceFiles, static source => Assert.Equal(source.Value, File.ReadAllBytes(source.Key)));

            spec.Build.PowerShellCompilation!.Enabled = false;
            Assert.Equal(moduleName, runner.Plan(spec).ModuleName);
            Assert.False(Directory.Exists(staging));
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }
}
