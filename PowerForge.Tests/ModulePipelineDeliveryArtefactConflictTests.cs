using System;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModulePipelineDeliveryArtefactConflictTests
{
    [Fact]
    public void Plan_DeliveryInternalsPathInsideExcludedArtefactsFolder_FailsFast()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                runner.Plan(CreateSpec(root.FullName, moduleName, deliveryInternalsPath: "Artefacts")));

            Assert.Contains("Delivery configuration is unsafe", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Delivery.InternalsPath 'Artefacts'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uses excluded directory name(s): Artefacts", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DeliveryInternalsPathOverlappingArtefactOutput_FailsFast()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                runner.Plan(CreateSpec(
                    root.FullName,
                    moduleName,
                    deliveryInternalsPath: "PackageFiles",
                    artefactPath: Path.Combine("PackageFiles", "Unpacked"),
                    requiredModulesPath: "Modules")));

            Assert.Contains("Delivery configuration is unsafe", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps artefact output root for 'Unpacked'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(root.FullName, "PackageFiles", "Unpacked"), ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_SeparatedDeliveryAndArtefactRoots_DoesNotWarnAboutOverlap()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var logger = new BufferedLogger();
            var runner = new ModulePipelineRunner(logger);

            var error = Record.Exception(() => runner.Plan(CreateSpec(
                root.FullName,
                moduleName,
                deliveryInternalsPath: "Internals",
                artefactPath: Path.Combine("Artefacts", "Unpacked"),
                requiredModulesPath: "Modules")));

            Assert.Null(error);
            Assert.DoesNotContain(
                logger.Entries.Where(entry => entry.Level == "warn"),
                entry => entry.Message.Contains("overlaps artefact output root", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_CaseDistinctDeliveryAndArtefactRoots_DoNotOverlapOnCaseSensitiveFileSystem()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            if (FrameworkCompatibility.GetPathStringComparison(root.FullName) != StringComparison.Ordinal)
                return;

            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());

            var error = Record.Exception(() => runner.Plan(CreateSpec(
                root.FullName,
                moduleName,
                deliveryInternalsPath: "Internals",
                artefactPath: Path.Combine("internals", "Unpacked"),
                requiredModulesPath: "Modules")));

            Assert.Null(error);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DeliveryInternalsPathUnset_UsesDefaultInternalsAndFailsFastOnOverlap()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                runner.Plan(CreateSpec(
                    root.FullName,
                    moduleName,
                    deliveryInternalsPath: string.Empty,
                    artefactPath: Path.Combine("Internals", "Unpacked"),
                    requiredModulesPath: "Modules")));

            Assert.Contains("Delivery.InternalsPath 'Internals'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps artefact output root for 'Unpacked'", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_ScriptNestedLayoutOverlappingDeliverySource_FailsFast(bool overlapThroughModulesPath)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string deliveryRoot = Path.Combine(root.FullName, "Internals");
            var runner = new ModulePipelineRunner(new NullLogger());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                runner.Plan(CreateSpec(
                    root.FullName,
                    moduleName,
                    deliveryInternalsPath: "Internals",
                    artefactPath: Path.Combine("Artefacts", "Script"),
                    requiredModulesPath: overlapThroughModulesPath ? null : deliveryRoot,
                    requiredModulesEnabled: !overlapThroughModulesPath,
                    artefactType: ArtefactType.Script,
                    modulesPath: overlapThroughModulesPath ? deliveryRoot : null)));

            Assert.Contains("Delivery configuration is unsafe", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                overlapThroughModulesPath ? "module copy root for 'Script'" : "required modules root for 'Script'",
                ex.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(deliveryRoot, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_ExternalDeliveryPath_DoesNotTripExcludedDirectoryCheck()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var externalDelivery = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Artefacts", Guid.NewGuid().ToString("N"), "Delivery"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());

            var error = Record.Exception(() => runner.Plan(CreateSpec(
                root.FullName,
                moduleName,
                deliveryInternalsPath: externalDelivery.FullName,
                excludedDirectories: new[] { "Artefacts" })));

            Assert.Null(error);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { externalDelivery.Parent?.Parent?.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DisabledRequiredModulesPathOverlap_DoesNotFail()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());

            var error = Record.Exception(() => runner.Plan(CreateSpec(
                root.FullName,
                moduleName,
                deliveryInternalsPath: "PackageFiles",
                artefactPath: Path.Combine("Artefacts", "Unpacked"),
                requiredModulesPath: "PackageFiles",
                requiredModulesEnabled: false)));

            Assert.Null(error);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Script)]
    public void Plan_DisabledRequiredModulesAbsolutePathUsedAsMainModuleRootOverlappingDelivery_FailsFast(
        ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string deliveryRoot = Path.Combine(root.FullName, "Internals");
            var runner = new ModulePipelineRunner(new NullLogger());

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Plan(CreateSpec(
                root.FullName,
                moduleName,
                deliveryInternalsPath: "Internals",
                artefactPath: Path.Combine("Artefacts", artefactType.ToString()),
                requiredModulesPath: deliveryRoot,
                requiredModulesEnabled: false,
                artefactType: artefactType)));

            Assert.Contains("Delivery configuration is unsafe", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"module copy root for '{artefactType}'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(deliveryRoot, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelineSpec CreateSpec(
        string root,
        string moduleName,
        string deliveryInternalsPath,
        string? artefactPath = null,
        string? requiredModulesPath = null,
        string[]? excludedDirectories = null,
        bool? requiredModulesEnabled = null,
        ArtefactType artefactType = ArtefactType.Unpacked,
        string? modulesPath = null)
    {
        var build = new ModuleBuildSpec
        {
            Name = moduleName,
            SourcePath = root,
            Version = "1.0.0",
            KeepStaging = true
        };

        if (excludedDirectories is not null)
            build.ExcludeDirectories = excludedDirectories;

        var segments = new System.Collections.Generic.List<IConfigurationSegment>
        {
            new ConfigurationOptionsSegment
            {
                Options = new ConfigurationOptions
                {
                    Delivery = new DeliveryOptionsConfiguration
                    {
                        Enable = true,
                        InternalsPath = deliveryInternalsPath
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(artefactPath))
        {
            segments.Add(new ConfigurationArtefactSegment
            {
                ArtefactType = artefactType,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = artefactPath,
                    RequiredModules = new ArtefactRequiredModulesConfiguration
                    {
                        Enabled = requiredModulesEnabled,
                        Path = requiredModulesPath,
                        ModulesPath = modulesPath
                    }
                }
            });
        }

        return new ModulePipelineSpec
        {
            Build = build,
            Install = new ModulePipelineInstallOptions
            {
                Enabled = false
            },
            Segments = segments.ToArray()
        };
    }

    private static void WriteMinimalModule(string root, string moduleName, string version)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, $"{moduleName}.psd1"),
            $"@{{ ModuleVersion = '{version}'; RootModule = '{moduleName}.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }}");
        File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
    }
}
