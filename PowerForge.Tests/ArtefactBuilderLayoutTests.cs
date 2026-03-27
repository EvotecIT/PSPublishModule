using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderLayoutTests
{
    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_Honors_Configured_Module_And_Dependency_Paths(ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var originalPsModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        string? inspectionRoot = null;

        try
        {
            const string moduleName = "DemoModule";
            const string dependencyName = "DependencyModule";
            const string dependencyVersion = "2.1.0";

            var stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging"));
            WriteStagingFixture(stagingRoot.FullName, moduleName);

            var localModulesRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "psmodules"));
            WriteInstalledModuleFixture(localModulesRoot.FullName, dependencyName, dependencyVersion);

            var updatedPsModulePath = string.Join(
                Path.PathSeparator,
                new[] { localModulesRoot.FullName, originalPsModulePath }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            Environment.SetEnvironmentVariable("PSModulePath", updatedPsModulePath);

            var outputRoot = Path.Combine(root.FullName, "Artefacts", artefactType.ToString());
            var mainModulesPath = Path.Combine(outputRoot, "Payload", "MainModules");
            var dependencyModulesPath = Path.Combine(outputRoot, "Payload", "Dependencies");

            var segment = new ConfigurationArtefactSegment
            {
                ArtefactType = artefactType,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = outputRoot,
                    RequiredModules = new ArtefactRequiredModulesConfiguration
                    {
                        Enabled = true,
                        ModulesPath = mainModulesPath,
                        Path = dependencyModulesPath,
                        Source = RequiredModulesSource.Installed
                    }
                }
            };

            var builder = new ArtefactBuilder(new NullLogger());
            var result = builder.Build(
                segment,
                projectRoot: root.FullName,
                stagingPath: stagingRoot.FullName,
                moduleName: moduleName,
                moduleVersion: "1.0.0",
                preRelease: null,
                requiredModules: new[] { new RequiredModuleReference(dependencyName, moduleVersion: dependencyVersion) });

            inspectionRoot = artefactType == ArtefactType.Packed
                ? Path.Combine(root.FullName, "Extracted")
                : result.OutputPath;

            if (artefactType == ArtefactType.Packed)
                ZipFile.ExtractToDirectory(result.OutputPath, inspectionRoot);

            Assert.True(File.Exists(Path.Combine(inspectionRoot, "Payload", "MainModules", moduleName, moduleName + ".psd1")));
            Assert.True(File.Exists(Path.Combine(inspectionRoot, "Payload", "Dependencies", dependencyName, dependencyName + ".psd1")));

            Assert.False(Directory.Exists(Path.Combine(inspectionRoot, moduleName)));
            Assert.False(Directory.Exists(Path.Combine(inspectionRoot, dependencyName)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", originalPsModulePath);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteStagingFixture(string stagingRoot, string moduleName)
    {
        File.WriteAllText(
            Path.Combine(stagingRoot, moduleName + ".psd1"),
            "@{ RootModule = '" + moduleName + ".psm1'; ModuleVersion = '1.0.0'; GUID = '" + Guid.NewGuid().ToString() + "' }");
        File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "function Get-Test { 'ok' }");
    }

    private static void WriteInstalledModuleFixture(string modulesRoot, string moduleName, string moduleVersion)
    {
        var versionRoot = Directory.CreateDirectory(Path.Combine(modulesRoot, moduleName, moduleVersion));
        File.WriteAllText(
            Path.Combine(versionRoot.FullName, moduleName + ".psd1"),
            "@{ RootModule = '" + moduleName + ".psm1'; ModuleVersion = '" + moduleVersion + "'; GUID = '" + Guid.NewGuid().ToString() + "' }");
        File.WriteAllText(Path.Combine(versionRoot.FullName, moduleName + ".psm1"), "function Get-Dependency { 'ok' }");
    }
}
