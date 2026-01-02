using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineExportAssemblyInferenceTests
{
    [Fact]
    public void Plan_InferExportAssembly_FromLegacyProjectName_WhenNotExplicitlyProvided()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    ExportAssemblies = Array.Empty<string>()
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            ProjectName = "PSParseHTML.PowerShell"
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "PSParseHTML.PowerShell" }, plan.BuildSpec.ExportAssemblies);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

