using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleTestSuitePreparationServiceTests
{
    [Fact]
    public void Prepare_applies_cicd_defaults_and_builds_spec()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-testsuite-prepare-" + Guid.NewGuid().ToString("N")));

        try
        {
            var context = new ModuleTestSuitePreparationService().Prepare(new ModuleTestSuitePreparationRequest
            {
                CurrentPath = root.FullName,
                ProjectPath = root.FullName,
                AdditionalModules = new[] { "Pester", " PSWriteColor ", "Pester" },
                SkipModules = new[] { " PSWriteColor " },
                TestPath = "Tests",
                OutputFormat = ModuleTestSuiteOutputFormat.Detailed,
                TimeoutSeconds = 123,
                EnableCodeCoverage = true,
                Force = true,
                SkipDependencies = true,
                SkipImport = true,
                CICD = true
            });

            Assert.Equal(root.FullName, context.ProjectRoot);
            Assert.True(context.PassThru);
            Assert.True(context.ExitOnFailure);
            Assert.Equal(ModuleTestSuiteOutputFormat.Minimal, context.Spec.OutputFormat);
            Assert.Equal(root.FullName, context.Spec.ProjectPath);
            Assert.Equal("Tests", context.Spec.TestPath);
            Assert.Equal(new[] { "Pester", "PSWriteColor" }, context.Spec.AdditionalModules);
            Assert.Equal(new[] { "PSWriteColor" }, context.Spec.SkipModules);
            Assert.Equal(123, context.Spec.TimeoutSeconds);
            Assert.True(context.Spec.EnableCodeCoverage);
            Assert.True(context.Spec.Force);
            Assert.True(context.Spec.SkipDependencies);
            Assert.True(context.Spec.SkipImport);
            Assert.True(context.Spec.PreferPwsh);
            Assert.False(context.Spec.KeepResultsXml);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_throws_for_missing_directory()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pf-testsuite-missing-" + Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<DirectoryNotFoundException>(() =>
            new ModuleTestSuitePreparationService().Prepare(new ModuleTestSuitePreparationRequest
            {
                CurrentPath = Directory.GetCurrentDirectory(),
                ProjectPath = missing
            }));

        Assert.Contains(missing, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
