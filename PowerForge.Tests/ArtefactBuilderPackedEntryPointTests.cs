using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderPackedEntryPointTests
{
    [Fact]
    public void Build_PackedFinalizerUsesLegacyModuleToProcessEntryPoint()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "LegacyModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ ModuleToProcess = 'LegacyModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingRoot, moduleName + ".psm1"), "function Get-LegacyValue { 'ok' }");
            string? observedEntryPoint = null;

            _ = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(root.FullName, "output"),
                        ArtefactName = moduleName + ".zip"
                    }
                },
                root.FullName,
                stagingRoot,
                moduleName,
                "1.0.0",
                null,
                Array.Empty<RequiredModuleReference>(),
                finalizePackedArtefact: context =>
                {
                    observedEntryPoint = context.EntryPointPath;
                    Assert.True(File.Exists(context.EntryPointPath));
                    Assert.NotEqual(context.ManifestPath, context.EntryPointPath);
                    Assert.Contains("Get-LegacyValue", File.ReadAllText(context.EntryPointPath), StringComparison.Ordinal);
                    return Array.Empty<string>();
                });

            Assert.NotNull(observedEntryPoint);
            Assert.Equal(moduleName + ".psm1", Path.GetFileName(observedEntryPoint));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
