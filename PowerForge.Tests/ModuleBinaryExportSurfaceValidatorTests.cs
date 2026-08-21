using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleBinaryExportSurfaceValidatorTests
{
    [Fact]
    public void Detect_AcceptsMatchingSideBySidePayloadSurfaces()
    {
        var root = CreateModulePayloads(
            typeof(InvokePowerForgePluginExportCommand).Assembly.Location,
            typeof(InvokePowerForgePluginExportCommand).Assembly.Location);

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", Array.Empty<string>());

            Assert.True(surface.HasAssemblies);
            Assert.Contains("Invoke-PowerForgePluginExport", surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_IgnoresPayloadLikeDirectoriesThatRuntimeSelectionCannotUse()
    {
        var root = CreateModulePayloads(
            typeof(InvokePowerForgePluginExportCommand).Assembly.Location,
            typeof(InvokePowerForgePluginExportCommand).Assembly.Location);
        Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core-backup"));

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", Array.Empty<string>());

            Assert.True(surface.HasAssemblies);
            Assert.Contains("Invoke-PowerForgePluginExport", surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_DoesNotLetArchivePayloadSatisfySelectableCorePayload()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);
        var libRoot = Path.Combine(root.FullName, "Lib");
        Directory.Delete(Path.Combine(libRoot, "Core-net10.0"), recursive: true);
        File.Delete(Path.Combine(libRoot, "Core", "DemoModule.dll"));
        var archive = Directory.CreateDirectory(Path.Combine(libRoot, "Core-backup"));
        File.Copy(typeof(BinaryExportDetector).Assembly.Location, Path.Combine(archive.FullName, "DemoModule.dll"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(root.FullName, "DemoModule", Array.Empty<string>()));

            Assert.Contains("Core", exception.Message, StringComparison.Ordinal);
            Assert.Contains("DemoModule.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_IgnoresLoneArchivePayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core-backup"));
        File.Copy(typeof(BinaryExportDetector).Assembly.Location, Path.Combine(archive.FullName, "DemoModule.dll"));

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", Array.Empty<string>());

            Assert.False(surface.HasAssemblies);
            Assert.Empty(surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_RejectsEmptySelectablePayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", Array.Empty<string>()));

            Assert.Contains("Core", exception.Message, StringComparison.Ordinal);
            Assert.Contains("DemoModule.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_RejectsEmptySelectablePayloadButIgnoresLoneArchive()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var core = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));

        try
        {
            Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(root.FullName, "DemoModule", Array.Empty<string>()));

            Directory.Delete(core.FullName, recursive: true);
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core-backup"));
            File.Copy(typeof(BinaryExportDetector).Assembly.Location, Path.Combine(archive.FullName, "DemoModule.dll"));

            ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(root.FullName, "DemoModule", Array.Empty<string>());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_RejectsDivergentSideBySidePayloadSurfaces()
    {
        var root = CreateModulePayloads(
            typeof(InvokePowerForgePluginExportCommand).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", Array.Empty<string>()));

            Assert.Contains("Binary export surfaces must match", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Core-net10.0", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Invoke-PowerForgePluginExport", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_RejectsMissingExportAssemblyInOnePayloadEvenWhenNoCmdletsAreDetected()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);
        File.Delete(Path.Combine(root.FullName, "Lib", "Core-net10.0", "DemoModule.dll"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", Array.Empty<string>()));

            Assert.Contains("export assemblies", exception.Message, StringComparison.Ordinal);
            Assert.Contains("DemoModule.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_RejectsNestedAssemblyWithoutPayloadRootAssembly()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);
        var newerRoot = Path.Combine(root.FullName, "Lib", "Core-net10.0");
        File.Delete(Path.Combine(newerRoot, "DemoModule.dll"));
        var nestedRoot = Directory.CreateDirectory(Path.Combine(newerRoot, "runtimes", "win-x64", "lib", "net10.0"));
        File.Copy(typeof(BinaryExportDetector).Assembly.Location, Path.Combine(nestedRoot.FullName, "DemoModule.dll"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(root.FullName, "DemoModule", Array.Empty<string>()));

            Assert.Contains("Core-net10.0", exception.Message, StringComparison.Ordinal);
            Assert.Contains("DemoModule.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_RejectsPayloadRootAssemblyWithWrongCasing()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);
        var newerRoot = Path.Combine(root.FullName, "Lib", "Core-net10.0");
        File.Delete(Path.Combine(newerRoot, "DemoModule.dll"));
        File.Copy(typeof(BinaryExportDetector).Assembly.Location, Path.Combine(newerRoot, "demomodule.dll"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(root.FullName, "DemoModule", Array.Empty<string>()));

            Assert.Contains("Core-net10.0", exception.Message, StringComparison.Ordinal);
            Assert.Contains("DemoModule.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_MatchesExplicitAssemblyNamesCaseInsensitively()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);

        try
        {
            ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(
                root.FullName,
                "DemoModule",
                new[] { "demomodule.dll" });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_MatchesExplicitAssemblyExtensionsCaseInsensitively()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);
        foreach (var payloadName in new[] { "Core", "Core-net10.0" })
        {
            var payloadRoot = Path.Combine(root.FullName, "Lib", payloadName);
            var temporaryPath = Path.Combine(payloadRoot, "DemoModule.rename");
            File.Move(
                Path.Combine(payloadRoot, "DemoModule.dll"),
                temporaryPath);
            File.Move(temporaryPath, Path.Combine(payloadRoot, "DemoModule.DLL"));
        }

        try
        {
            ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(
                root.FullName,
                "DemoModule",
                new[] { "demomodule.dll" });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_RejectsConfiguredExportAssemblyMissingFromEveryPayload()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.Detect(
                    root.FullName,
                    "DemoModule",
                    new[] { "DemoModule.dll", "Helpers.dll" }));

            Assert.Contains("missing configured export assemblies", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Helpers.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("subdirectory/DemoModule.dll")]
    [InlineData("subdirectory\\DemoModule.dll")]
    [InlineData("C:DemoModule.dll")]
    [InlineData("C:\\exports\\DemoModule.dll")]
    public void Detect_RejectsRelativeExportAssemblyPathsForSideBySidePayloads(string configuredPath)
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", new[] { configuredPath }));

            Assert.Contains("Path-qualified export assemblies are ambiguous", exception.Message, StringComparison.Ordinal);
            Assert.Contains(configuredPath, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_RejectsAbsoluteExportAssemblyPathForSideBySidePayloads()
    {
        var root = CreateModulePayloads(
            typeof(BinaryExportDetector).Assembly.Location,
            typeof(BinaryExportDetector).Assembly.Location);
        var configuredPath = Path.Combine(root.FullName, "external", "DemoModule.dll");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", new[] { configuredPath }));

            Assert.Contains("Path-qualified export assemblies are ambiguous", exception.Message, StringComparison.Ordinal);
            Assert.Contains(configuredPath, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_HonorsRelativeExportAssemblyPathForSingleSelectablePayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var relativePath = Path.Combine("Lib", "Core", "DemoModule.dll");
        var assemblyPath = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
        File.Copy(typeof(InvokePowerForgePluginExportCommand).Assembly.Location, assemblyPath);

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", new[] { relativePath });

            Assert.True(surface.HasAssemblies);
            Assert.Contains("Invoke-PowerForgePluginExport", surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_HonorsAbsoluteExportAssemblyPathForSingleSelectablePayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var payloadRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
        var assemblyPath = Path.Combine(payloadRoot.FullName, "DemoModule.dll");
        File.Copy(typeof(InvokePowerForgePluginExportCommand).Assembly.Location, assemblyPath);

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", new[] { assemblyPath });

            Assert.True(surface.HasAssemblies);
            Assert.Contains("Invoke-PowerForgePluginExport", surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_HonorsAbsoluteExportAssemblyPathForSinglePayloadLayout()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var configuredPath = typeof(InvokePowerForgePluginExportCommand).Assembly.Location;

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", new[] { configuredPath });

            Assert.True(surface.HasAssemblies);
            Assert.Contains("Invoke-PowerForgePluginExport", surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Detect_HonorsRelativeExportAssemblyPathForSinglePayloadLayout()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var relativePath = Path.Combine("exports", "DemoModule.dll");
        var assemblyPath = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
        File.Copy(typeof(InvokePowerForgePluginExportCommand).Assembly.Location, assemblyPath);

        try
        {
            var surface = ModuleBinaryExportSurfaceValidator.Detect(root.FullName, "DemoModule", new[] { relativePath });

            Assert.True(surface.HasAssemblies);
            Assert.Contains("Invoke-PowerForgePluginExport", surface.Cmdlets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DirectoryInfo CreateModulePayloads(string baselineAssembly, string newerAssembly)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var core = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
        var newer = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core-net10.0"));
        File.Copy(baselineAssembly, Path.Combine(core.FullName, "DemoModule.dll"));
        File.Copy(newerAssembly, Path.Combine(newer.FullName, "DemoModule.dll"));
        return root;
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch
        {
            // Best effort cleanup for test artifacts.
        }
    }
}
