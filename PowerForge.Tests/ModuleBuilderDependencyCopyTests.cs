using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleBuilderDependencyCopyTests
{
    [Fact]
    public void ComputeExcludedLibraries_DoesNotCascadeIntoNonPowerShellDependencies()
    {
        const string json = """
                            {
                              "targets": {
                                ".NETFramework,Version=v4.7.2": {
                                  "Microsoft.PowerShell.5.ReferenceAssemblies/1.0.0": {
                                    "dependencies": {
                                      "System.Text.Json": "9.0.0"
                                    }
                                  },
                                  "System.Text.Json/9.0.0": {
                                    "dependencies": {
                                      "System.IO.Pipelines": "9.0.0"
                                    }
                                  },
                                  "System.IO.Pipelines/9.0.0": {}
                                }
                              }
                            }
                            """;

        var method = typeof(ModuleBuilder).GetMethod("ComputeExcludedLibraries", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var doc = JsonDocument.Parse(json);
        var targets = doc.RootElement.GetProperty("targets");
        var excluded = (HashSet<string>)method!.Invoke(null, new object[] { targets })!;

        Assert.Contains(excluded, value => string.Equals(value, "Microsoft.PowerShell.5.ReferenceAssemblies/1.0.0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(excluded, value => string.Equals(value, "System.Text.Json/9.0.0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(excluded, value => string.Equals(value, "System.IO.Pipelines/9.0.0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CopyPublishOutputBinaries_IncludesTopLevelFallbackFiles_WhenDepsMissesThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(root, "publish");
        var targetDir = Path.Combine(root, "target");

        Directory.CreateDirectory(publishDir);
        Directory.CreateDirectory(targetDir);

        try
        {
            var mainDll = Path.Combine(publishDir, "TestModule.dll");
            var extraDll = Path.Combine(publishDir, "System.IO.Pipelines.dll");
            File.WriteAllText(mainDll, "a");
            File.WriteAllText(extraDll, "b");

            var depsPath = Path.Combine(publishDir, "TestModule.deps.json");
            File.WriteAllText(depsPath, """
                                        {
                                          "targets": {
                                            ".NETFramework,Version=v4.7.2": {
                                              "TestModule/1.0.0": {
                                                "runtime": {
                                                  "TestModule.dll": {}
                                                }
                                              }
                                            }
                                          }
                                        }
                                        """);

            var builder = new ModuleBuilder(new NullLogger());
            var copyMethod = typeof(ModuleBuilder).GetMethod(
                "CopyPublishOutputBinaries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(copyMethod);

            copyMethod!.Invoke(builder, new object[]
            {
                publishDir,
                targetDir,
                "net472",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            });

            Assert.True(File.Exists(Path.Combine(targetDir, "TestModule.dll")));
            Assert.True(File.Exists(Path.Combine(targetDir, "System.IO.Pipelines.dll")));
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
                // best effort cleanup
            }
        }
    }
}
