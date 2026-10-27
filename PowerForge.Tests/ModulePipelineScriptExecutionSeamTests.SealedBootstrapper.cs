using System.IO.Compression;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(ModuleDevelopmentBinaryMode.Auto, false)]
    [InlineData(ModuleDevelopmentBinaryMode.Environment, false)]
    [InlineData(ModuleDevelopmentBinaryMode.Off, false)]
    [InlineData(ModuleDevelopmentBinaryMode.Auto, true)]
    [InlineData(ModuleDevelopmentBinaryMode.Environment, true)]
    [InlineData(ModuleDevelopmentBinaryMode.Off, true)]
    public void Run_SealedReleasePreservesSourceLoaderAndPackagesCurrentBootstrapper(ModuleDevelopmentBinaryMode mode, bool releaseCheckpoint)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var source = Path.Combine(root.FullName, "source");
            WriteMinimalModule(source, moduleName, "1.0.0");
            Directory.CreateDirectory(Path.Combine(source, "Public"));
            File.WriteAllText(Path.Combine(source, "Public", "Get-Test.ps1"), "function Get-Test { 'fixture' }");
            var binaryRoot = Path.Combine(root.FullName, "Binaries");
            ModuleBootstrapperGenerator.Generate(
                source, moduleName,
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "TestModule.Binary.dll" }, handleRuntimes: false, useAssemblyLoadContext: true,
                developmentBinaries: new ModuleDevelopmentBinaryBootstrapperOptions(
                    ModuleDevelopmentBinaryMode.Auto, binaryRoot, "TEST_MODULE_DEV", "TEST_MODULE_CONFIGURATION",
                    new[] { "net8.0" }, new[] { "net472" }));
            var sourceLoader = Path.Combine(source, moduleName + ".psm1");
            File.AppendAllText(sourceLoader, "\n# Retained source loader revision.\n", new UTF8Encoding(false));
            var original = File.ReadAllBytes(sourceLoader);
            RunGit(source, "init", "--quiet");
            RunGit(source, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "--quiet", "-m", "fixture");

            var staging = Path.Combine(root.FullName, "staging");
            var packed = Path.Combine(root.FullName, "packed");
            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(), new FakeHostedOperations());
            var result = runner.Run(new ModulePipelineSpec
            {
                ReleaseCheckpoint = releaseCheckpoint,
                Build = new ModuleBuildSpec
                {
                    Name = moduleName, SourcePath = source, Version = "1.0.0", StagingPath = staging,
                    KeepStaging = true, DevelopmentBinariesMode = mode, DevelopmentBinariesPath = binaryRoot,
                    DevelopmentSourceBootstrapperMode = ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    CreateReleaseProtection(requireSourceUnchanged: !releaseCheckpoint),
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration { Enabled = true, Path = packed }
                    }
                }
            });

            Assert.Equal(!releaseCheckpoint, result.Plan.RequireReleaseSourceUnchanged);
            Assert.Equal(releaseCheckpoint, result.Plan.ReleaseCheckpoint);
            Assert.Equal(original, File.ReadAllBytes(sourceLoader));
            var stagedLoader = File.ReadAllBytes(Path.Combine(staging, moduleName + ".psm1"));
            Assert.DoesNotContain("Retained source loader revision", Encoding.UTF8.GetString(stagedLoader), StringComparison.Ordinal);
            Assert.Contains("Get-Test", Encoding.UTF8.GetString(stagedLoader), StringComparison.Ordinal);
            using var archive = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(packed, "*.zip", SearchOption.AllDirectories)));
            using var entry = Assert.Single(archive.Entries, item => item.Name == moduleName + ".psm1").Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            Assert.Equal(stagedLoader, bytes.ToArray());
        }
        finally
        {
            // Git creates read-only object files in this invocation-owned fixture.
            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
                if (file.IsReadOnly) file.IsReadOnly = false;
            root.Delete(recursive: true);
        }
    }
}
