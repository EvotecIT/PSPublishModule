using System;
using System.IO;
using System.Reflection;

namespace PowerForge.Tests;

public sealed class ModuleManifestValueReaderTests
{
    [Fact]
    public void ReadPsDataStringOrArray_ParsesSingleLinePsDataArrays()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "PowerForgeManifestValueReaderTests", Path.GetRandomFileName());
        Directory.CreateDirectory(projectRoot);

        try
        {
            var manifestPath = Path.Combine(projectRoot, "Sample.psd1");
            File.WriteAllText(
                manifestPath,
                """
                @{
                    RequiredModules = @('LegacyOnly', 'Microsoft.PowerShell.Utility', 'Az.Accounts')
                    PrivateData = @{
                        PSData = @{
                            ExternalModuleDependencies = @('Old.External', 'Az.Accounts')
                        }
                    }
                }
                """);

            var readerType = typeof(ModuleInformationReader).Assembly.GetType("PowerForge.ModuleManifestValueReader");
            Assert.NotNull(readerType);

            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var readRequiredModules = readerType!.GetMethod("ReadRequiredModules", flags);
            var readPsDataStringOrArray = readerType.GetMethod("ReadPsDataStringOrArray", flags);
            Assert.NotNull(readRequiredModules);
            Assert.NotNull(readPsDataStringOrArray);

            var requiredModules = Assert.IsType<RequiredModuleReference[]>(readRequiredModules!.Invoke(null, new object[] { manifestPath }));
            var externalModules = Assert.IsType<string[]>(readPsDataStringOrArray!.Invoke(null, new object[] { manifestPath, "ExternalModuleDependencies" }));

            Assert.Equal(
                new[] { "LegacyOnly", "Microsoft.PowerShell.Utility", "Az.Accounts" },
                requiredModules.Select(static module => module.ModuleName).ToArray());
            Assert.Equal(new[] { "Old.External", "Az.Accounts" }, externalModules);
        }
        finally
        {
            try { Directory.Delete(projectRoot, recursive: true); } catch { }
        }
    }
}
