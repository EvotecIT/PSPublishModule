using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class ModuleBuilderMultiTargetTests
{
    [Fact]
    public void BuildInPlace_ValidatesPayloadLayoutBeforeReplacingExistingLib()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
        var projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "DemoModule"));
        var projectPath = Path.Combine(projectRoot.FullName, "DemoModule.csproj");
        var existingLib = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
        var sentinelPath = Path.Combine(existingLib.FullName, "known-good.dll");
        File.WriteAllText(sentinelPath, "known-good");
        File.WriteAllText(Path.Combine(moduleRoot.FullName, "DemoModule.psm1"), string.Empty);
        File.WriteAllText(Path.Combine(moduleRoot.FullName, "DemoModule.psd1"), "@{ RootModule = 'DemoModule.psm1'; ModuleVersion = '1.0.0' }");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

        try
        {
            var builder = ModuleBuilderTestDependencies.Create();
            Assert.Throws<InvalidOperationException>(() => builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = moduleRoot.FullName,
                ModuleName = "DemoModule",
                CsprojPath = projectPath,
                Frameworks = new[] { "net8.0", "net10.0-windows" },
                DisableBinaryCmdletScan = true,
            }));

            Assert.Equal("known-good", File.ReadAllText(sentinelPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildInPlace_ValidatesEveryPayloadWhenBinaryCmdletScanIsDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
        var core = Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(moduleRoot.FullName, "Lib", "Core-net10.0"));
        File.Copy(typeof(ModuleBuilder).Assembly.Location, Path.Combine(core.FullName, "DemoModule.dll"));
        File.WriteAllText(Path.Combine(moduleRoot.FullName, "DemoModule.psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot.FullName, "DemoModule.psd1"),
            "@{ RootModule = 'DemoModule.psm1'; ModuleVersion = '1.0.0'; CmdletsToExport = @(); AliasesToExport = @() }");

        try
        {
            var builder = ModuleBuilderTestDependencies.Create();
            var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = moduleRoot.FullName,
                ModuleName = "DemoModule",
                DisableBinaryCmdletScan = true,
            }));

            Assert.Contains("Core-net10.0", exception.Message, StringComparison.Ordinal);
            Assert.Contains("DemoModule.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

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
