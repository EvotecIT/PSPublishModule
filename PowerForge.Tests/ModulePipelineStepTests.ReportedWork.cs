using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineStepTests
{
    [Theory]
    [InlineData(AssemblyTypeAcceleratorExportMode.None)]
    [InlineData(AssemblyTypeAcceleratorExportMode.Assembly)]
    public void Create_PlansDependencyAndTypeAcceleratorWorkAsSteps(AssemblyTypeAcceleratorExportMode typeAccelerators)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0", CsprojPath = null },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
            plan.BuildSpec.AssemblyTypeAcceleratorMode = typeAccelerators;
            var keys = Array.ConvertAll(ModulePipelineStep.Create(plan), static step => step.Key);

            // Dependency preparation runs before staging; the report runs after the module is final and before docs.
            Assert.InRange(Array.IndexOf(keys, "deps:install"), 0, Array.IndexOf(keys, "build:stage") - 1);
            var report = Array.IndexOf(keys, "build:type-accelerators");
            if (typeAccelerators == AssemblyTypeAcceleratorExportMode.None)
                Assert.Equal(-1, report);
            else
                Assert.True(report > Array.IndexOf(keys, "build:manifest"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
