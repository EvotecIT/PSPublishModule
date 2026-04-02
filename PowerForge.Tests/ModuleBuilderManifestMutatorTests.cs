using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModuleBuilderManifestMutatorTests
{
    [Fact]
    public void BuildInPlace_UsesManifestMutatorForManifestUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "TestModule";
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator);

            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
                Author = "Author",
                CompanyName = "Company",
                Description = "Description",
                CompatiblePSEditions = new[] { "Desktop", "Core" },
                Tags = new[] { "TagOne", "TagTwo" },
                IconUri = "https://example.com/icon.png",
                ProjectUri = "https://example.com/project"
            });

            Assert.Contains(mutator.TopLevelVersionWrites, static write => write.NewVersion == "2.0.0");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "RootModule" && write.Value == "TestModule.psm1");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "Author" && write.Value == "Author");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "CompanyName" && write.Value == "Company");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "Description" && write.Value == "Description");
            Assert.Contains(mutator.TopLevelStringArrayWrites, static write => write.Key == "CompatiblePSEditions" && write.Values.SequenceEqual(new[] { "Desktop", "Core" }));
            Assert.Contains(mutator.PsDataStringArrayWrites, static write => write.Key == "Tags" && write.Values.SequenceEqual(new[] { "TagOne", "TagTwo" }));
            Assert.Contains(mutator.PsDataStringWrites, static write => write.Key == "IconUri" && write.Value == "https://example.com/icon.png");
            Assert.Contains(mutator.PsDataStringWrites, static write => write.Key == "ProjectUri" && write.Value == "https://example.com/project");
            Assert.Contains(mutator.TopLevelStringArrayWrites, static write => write.Key == "FunctionsToExport");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private sealed class RecordingManifestMutator : IModuleManifestMutator
    {
        public List<(string FilePath, string NewVersion)> TopLevelVersionWrites { get; } = new();
        public List<(string FilePath, string Key, string Value)> TopLevelStringWrites { get; } = new();
        public List<(string FilePath, string Key, string[] Values)> TopLevelStringArrayWrites { get; } = new();
        public List<(string FilePath, string Key, string Value)> PsDataStringWrites { get; } = new();
        public List<(string FilePath, string Key, string[] Values)> PsDataStringArrayWrites { get; } = new();

        public bool TrySetTopLevelModuleVersion(string filePath, string newVersion)
        {
            TopLevelVersionWrites.Add((filePath, newVersion));
            return true;
        }

        public bool TrySetTopLevelString(string filePath, string key, string newValue)
        {
            TopLevelStringWrites.Add((filePath, key, newValue));
            return true;
        }

        public bool TrySetTopLevelStringArray(string filePath, string key, string[] values)
        {
            TopLevelStringArrayWrites.Add((filePath, key, values ?? Array.Empty<string>()));
            return true;
        }

        public bool TrySetPsDataString(string filePath, string key, string value)
        {
            PsDataStringWrites.Add((filePath, key, value));
            return true;
        }

        public bool TrySetPsDataStringArray(string filePath, string key, string[] values)
        {
            PsDataStringArrayWrites.Add((filePath, key, values ?? Array.Empty<string>()));
            return true;
        }
    }
}
