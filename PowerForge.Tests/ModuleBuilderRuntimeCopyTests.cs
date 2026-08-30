using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModuleBuilderDependencyCopyTests
{
    [Fact]
    public void CopyPublishOutputBinaries_PreservesSafeDeclaredRuntimeSubpaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(root, "publish");
        var targetDir = Path.Combine(root, "target");
        var managedRelativePath = Path.Combine("runtimes", "linux-x64", "lib", "net10.0", "Runtime.Helper.dll");
        var nativeRelativePath = Path.Combine("runtimes", "linux-x64", "native", "libnative.so");
        var escapedSource = Path.Combine(publishDir, "outside", "escaped.dll");

        Directory.CreateDirectory(Path.Combine(publishDir, "runtimes", "linux-x64", "lib", "net10.0"));
        Directory.CreateDirectory(Path.Combine(publishDir, "runtimes", "linux-x64", "native"));
        Directory.CreateDirectory(Path.GetDirectoryName(escapedSource)!);
        Directory.CreateDirectory(targetDir);

        try
        {
            File.WriteAllText(Path.Combine(publishDir, "TestModule.dll"), "module");
            File.WriteAllText(Path.Combine(publishDir, managedRelativePath), "managed-runtime");
            File.WriteAllText(Path.Combine(publishDir, nativeRelativePath), "native-runtime");
            File.WriteAllText(escapedSource, "escaped");
            File.WriteAllText(Path.Combine(publishDir, "TestModule.deps.json"), """
                {
                  "targets": {
                    ".NETCoreApp,Version=v10.0": {
                      "TestModule/1.0.0": {
                        "runtime": {
                          "TestModule.dll": {},
                          "runtimes/linux-x64/lib/net10.0/Runtime.Helper.dll": {}
                        },
                        "native": {
                          "runtimes/linux-x64/native/libnative.so": {}
                        },
                        "runtimeTargets": {
                          "runtimes/../outside/escaped.dll": {
                            "rid": "linux-x64",
                            "assetType": "native"
                          }
                        }
                      }
                    }
                  }
                }
                """);

            var builder = ModuleBuilderTestDependencies.Create();
            var copyMethod = typeof(ModuleBuilder).GetMethod(
                "CopyPublishOutputBinaries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var optionsType = typeof(ModuleBuilder).GetNestedType("PublishCopyOptions", BindingFlags.NonPublic);

            Assert.NotNull(copyMethod);
            Assert.NotNull(optionsType);

            var options = Activator.CreateInstance(
                optionsType!,
                new object[] { Array.Empty<string>(), false, true });

            copyMethod!.Invoke(builder, new object[]
            {
                publishDir,
                targetDir,
                "net10.0",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                options!
            });

            Assert.Equal("managed-runtime", File.ReadAllText(Path.Combine(targetDir, managedRelativePath)));
            Assert.Equal("native-runtime", File.ReadAllText(Path.Combine(targetDir, nativeRelativePath)));
            Assert.False(File.Exists(Path.Combine(targetDir, "outside", "escaped.dll")));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
