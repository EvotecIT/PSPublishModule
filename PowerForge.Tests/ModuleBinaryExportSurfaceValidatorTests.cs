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

    [Theory]
    [InlineData("subdirectory/DemoModule.dll")]
    [InlineData("subdirectory\\DemoModule.dll")]
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
