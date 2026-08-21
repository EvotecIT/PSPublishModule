using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class ModuleBuilderMultiTargetTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void BuildInPlace_PreservesNet8AndNet10Payloads()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
        var projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "DemoModule"));
        var projectPath = Path.Combine(projectRoot.FullName, "DemoModule.csproj");

        File.WriteAllText(Path.Combine(moduleRoot.FullName, "DemoModule.psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot.FullName, "DemoModule.psd1"),
            "@{ RootModule = 'DemoModule.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks><AssemblyName>DemoModule</AssemblyName></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projectRoot.FullName, "Demo.cs"), "public static class Demo { public static string Value => \"demo\"; }");

        try
        {
            var builder = ModuleBuilderTestDependencies.Create();
            _ = builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = moduleRoot.FullName,
                ModuleName = "DemoModule",
                CsprojPath = projectPath,
                Configuration = "Release",
                Frameworks = new[] { "net8.0", "net10.0" },
                ModuleVersion = "1.0.0",
                DisableBinaryCmdletScan = true,
                EmitBinaryConflictOwnerNotes = false,
            });

            Assert.True(File.Exists(Path.Combine(moduleRoot.FullName, "Lib", "Core", "DemoModule.dll")));
            Assert.True(File.Exists(Path.Combine(moduleRoot.FullName, "Lib", "Core-net10.0", "DemoModule.dll")));
            Assert.Equal(
                "net8.0",
                File.ReadAllText(Path.Combine(moduleRoot.FullName, "Lib", "Core", ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName)));
            Assert.Equal(
                "net10.0",
                File.ReadAllText(Path.Combine(moduleRoot.FullName, "Lib", "Core-net10.0", ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName)));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
