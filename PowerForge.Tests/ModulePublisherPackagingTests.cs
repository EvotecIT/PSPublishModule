using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherPackagingTests
{
    [Fact]
    public void PrepareModulePackageForRepositoryPublish_CopiesModuleLayoutOnly()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? publishPath = null;

        try
        {
            const string moduleName = "TestModule";
            WriteStagingFixture(root.FullName, moduleName);

            publishPath = ModulePublisher.PrepareModulePackageForRepositoryPublish(
                stagingPath: root.FullName,
                moduleName: moduleName,
                information: null,
                includeScriptFolders: true);

            Assert.NotNull(publishPath);
            Assert.True(File.Exists(Path.Combine(publishPath!, "TestModule.psd1")));
            Assert.True(File.Exists(Path.Combine(publishPath!, "TestModule.psm1")));
            Assert.True(File.Exists(Path.Combine(publishPath!, "Lib", "Default", "Binary.dll")));
            Assert.True(File.Exists(Path.Combine(publishPath!, "Public", "Get-Test.ps1")));

            Assert.False(Directory.Exists(Path.Combine(publishPath!, "Sources")));
            Assert.False(Directory.Exists(Path.Combine(publishPath!, ".github")));
            Assert.False(File.Exists(Path.Combine(publishPath!, "README.md")));
        }
        finally
        {
            ModulePublisher.CleanupTemporaryPublishPath(publishPath);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PrepareModulePackageForRepositoryPublish_RespectsIncludeScriptFoldersFlag()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? publishPath = null;

        try
        {
            const string moduleName = "TestModule";
            WriteStagingFixture(root.FullName, moduleName);

            publishPath = ModulePublisher.PrepareModulePackageForRepositoryPublish(
                stagingPath: root.FullName,
                moduleName: moduleName,
                information: null,
                includeScriptFolders: false);

            Assert.True(File.Exists(Path.Combine(publishPath!, "TestModule.psd1")));
            Assert.True(File.Exists(Path.Combine(publishPath!, "TestModule.psm1")));
            Assert.False(Directory.Exists(Path.Combine(publishPath!, "Public")));
        }
        finally
        {
            ModulePublisher.CleanupTemporaryPublishPath(publishPath);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteStagingFixture(string stagingRoot, string moduleName)
    {
        Directory.CreateDirectory(stagingRoot);

        File.WriteAllText(Path.Combine(stagingRoot, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");
        File.WriteAllText(Path.Combine(stagingRoot, $"{moduleName}.psm1"), string.Empty);
        File.WriteAllText(Path.Combine(stagingRoot, "README.md"), "not for package");

        Directory.CreateDirectory(Path.Combine(stagingRoot, "Lib", "Default"));
        File.WriteAllText(Path.Combine(stagingRoot, "Lib", "Default", "Binary.dll"), "binary");

        Directory.CreateDirectory(Path.Combine(stagingRoot, "Public"));
        File.WriteAllText(Path.Combine(stagingRoot, "Public", "Get-Test.ps1"), "function Get-Test { }");

        Directory.CreateDirectory(Path.Combine(stagingRoot, "Sources"));
        File.WriteAllText(Path.Combine(stagingRoot, "Sources", "Internal.cs"), "class Internal { }");

        Directory.CreateDirectory(Path.Combine(stagingRoot, ".github"));
        File.WriteAllText(Path.Combine(stagingRoot, ".github", "dependabot.yml"), "version: 2");
    }
}

