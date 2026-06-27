using PSPublishModule;
using System.Reflection;
using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ModuleStateInventoryCommandSupportTests
{
    [Fact]
    public void InferScope_RecognizesUnixAllUsersRoots()
    {
        var scope = InvokeInferScope("/usr/local/share/powershell/Modules/Company.Tools");

        Assert.Equal("AllUsers", scope);
    }

    [Fact]
    public void InferScope_DoesNotTreatArbitraryProfilePathsAsCurrentUser()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
            return;

        var scope = InvokeInferScopeNullable(Path.Combine(profile, "src", "Modules", "Company.Tools"));

        Assert.Null(scope);
    }

    [Fact]
    public void InferScope_DoesNotTreatArbitraryProgramFilesPathsAsAllUsers()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            return;

        var scope = InvokeInferScopeNullable(Path.Combine(programFiles, "Company", "StagingModules", "Company.Tools"));

        Assert.Null(scope);
    }

    [Fact]
    public void CreateInventoryResultFromFile_MarksMatchingLoadedModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var inventoryPath = Path.Combine(root.FullName, "inventory.json");
            var modulePath = Path.Combine(root.FullName, "Company.Tools", "1.2.0");
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(inventoryPath, $$"""
{
  "installedModules": [
    {
      "name": "Company.Tools",
      "version": "1.2.0",
      "path": "{{modulePath.Replace("\\", "\\\\")}}"
    }
  ]
}
""");

            var result = ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(
                inventoryPath,
                new[] { new ModuleStateLoadedModuleEvidence("Company.Tools", "1.2", Path.Combine(modulePath, "Company.Tools.psm1")) });

            var module = Assert.Single(result.InstalledModules);
            Assert.True(module.IsLoaded);
            Assert.True(module.IsEffectiveImportCandidate);
            Assert.Equal(modulePath, module.Path);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void CreateInventoryResultFromFile_AddsLoadedOnlyModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var inventoryPath = Path.Combine(root.FullName, "inventory.json");
            File.WriteAllText(inventoryPath, """{ "installedModules": [] }""");

            var result = ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(
                inventoryPath,
                new[] { new ModuleStateLoadedModuleEvidence("Company.Runtime", "2.0.0", @"C:\Temp\Company.Runtime.psm1") });

            var module = Assert.Single(result.InstalledModules);
            Assert.Equal("Company.Runtime", module.Name);
            Assert.Equal("2.0.0", module.Version);
            Assert.True(module.IsLoaded);
            Assert.False(module.IsEffectiveImportCandidate);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void IncludeLoadedModules_MergesLoadedEvidenceIntoPipedInventory()
    {
        var inventory = new ModuleStateInventoryResult
        {
            Source = "Pipeline",
            ModulePaths = new[] { @"C:\Modules" },
            InstalledModules = new[]
            {
                new ModuleStateInstalledModuleResult
                {
                    Name = "Company.Tools",
                    Version = "1.2.0",
                    Path = @"C:\Modules\Company.Tools\1.2.0"
                }
            }
        };

        var result = ModuleStateInventoryCommandSupport.IncludeLoadedModules(
            inventory,
            new[] { new ModuleStateLoadedModuleEvidence("Company.Tools", "1.2", @"C:\Modules\Company.Tools\1.2.0\Company.Tools.psm1") });

        Assert.Equal("Pipeline", result.Source);
        Assert.Equal(new[] { @"C:\Modules" }, result.ModulePaths);
        var module = Assert.Single(result.InstalledModules);
        Assert.True(module.IsLoaded);
        Assert.True(module.IsEffectiveImportCandidate);
        Assert.Equal(@"C:\Modules\Company.Tools\1.2.0", module.Path);
    }

    [Fact]
    public void ResolveLoadedModuleVersion_AppendsPrereleaseLabel()
    {
        var item = new PSObject();
        item.Properties.Add(new PSNoteProperty("Version", new Version(1, 2, 0)));
        item.Properties.Add(new PSNoteProperty("Prerelease", "preview1"));

        var version = ModuleStateInventoryCommandSupport.ResolveLoadedModuleVersion(item);

        Assert.Equal("1.2.0-preview1", version);
    }

    private static string? InvokeInferScope(string path)
    {
        var result = InvokeInferScopeNullable(path);
        return Assert.IsType<string>(result);
    }

    private static string? InvokeInferScopeNullable(string path)
    {
        var method = typeof(ModuleStateInventoryCommandSupport).GetMethod(
            "InferScope",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return method!.Invoke(null, new object[] { path }) as string;
    }
}
