using PSPublishModule;
using System.Reflection;

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
            Assert.True(module.IsEffectiveImportCandidate);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static string? InvokeInferScope(string path)
    {
        var method = typeof(ModuleStateInventoryCommandSupport).GetMethod(
            "InferScope",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<string>(method!.Invoke(null, new object[] { path }));
    }
}
