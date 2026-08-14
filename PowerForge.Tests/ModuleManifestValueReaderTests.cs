using System;
using System.IO;
using System.Reflection;

namespace PowerForge.Tests;

public sealed class ModuleManifestValueReaderTests
{
    [Fact]
    public void ReadTopLevelStringOrArrayFromText_IgnoresNestedMetadataWithSameKey()
    {
        const string manifest = """
            @{
                PrivateData = @{
                    RequiredAssemblies = @('MetadataOnly.dll')
                }
                RequiredAssemblies = @('Runtime.dll')
            }
            """;

        string[] values = ModuleManifestValueReader.ReadTopLevelStringOrArrayFromText(
            manifest,
            "RequiredAssemblies");

        Assert.Equal(new[] { "Runtime.dll" }, values);
        Assert.Empty(ModuleManifestValueReader.ReadTopLevelStringOrArrayFromText(
            "@{ PrivateData = @{ RequiredAssemblies = @('MetadataOnly.dll') } }",
            "RequiredAssemblies"));
    }

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

    [Fact]
    public void ReadPsDataStringOrArrayFromText_IgnoresNestedNonPsDataValue()
    {
        const string manifest = """
            @{
                ModuleVersion = '1.2.3'
                PrivateData = @{
                    Unrelated = @{
                        Prerelease = 'nested-preview'
                    }
                }
            }
            """;

        Assert.Empty(ModuleManifestValueReader.ReadPsDataStringOrArrayFromText(manifest, "Prerelease"));
    }

    [Fact]
    public void ReadTopLevelModuleReferencePathsFromText_ParsesStringsAndModuleSpecifications()
    {
        const string manifest = """
            @{
                NestedModules = @(
                    'First.psm1'
                    @{ ModuleName = 'Second.psm1'; ModuleVersion = '1.0' }
                )
            }
            """;

        Assert.Equal(
            new[] { "First.psm1", "Second.psm1" },
            ModuleManifestValueReader.ReadTopLevelModuleReferencePathsFromText(manifest, "NestedModules"));
    }

    [Fact]
    public void ReadTopLevelModuleReferencePathsFromText_RejectsDynamicModuleSpecifications()
    {
        const string manifest = "@{ NestedModules = @(@{ ModuleName = (Join-Path 'lib' 'Dynamic.psm1') }) }";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ModuleManifestValueReader.ReadTopLevelModuleReferencePathsFromText(manifest, "NestedModules"));

        Assert.Contains("literal ModuleName", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

}
