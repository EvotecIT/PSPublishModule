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
                                  "TestModule/1.0.0": {
                                    "dependencies": {
                                      "System.Text.Json": "9.0.0",
                                      "Microsoft.PowerShell.SDK": "7.4.1"
                                    },
                                    "runtime": {
                                      "TestModule.dll": {}
                                    }
                                  },
                                  "Microsoft.PowerShell.SDK/7.4.1": {
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
        var excluded = (HashSet<string>)method!.Invoke(null, new object[] { targets, "TestModule", Array.Empty<string>() })!;

        Assert.Contains(excluded, value => string.Equals(value, "Microsoft.PowerShell.SDK/7.4.1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(excluded, value => string.Equals(value, "System.Text.Json/9.0.0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(excluded, value => string.Equals(value, "System.IO.Pipelines/9.0.0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComputeExcludedLibraries_ExcludesWindowsCompatibilityClosure_WhenOnlyReachedThroughPowerShellSdk()
    {
        const string json = """
                            {
                              "targets": {
                                ".NETCoreApp,Version=v10.0": {
                                  "TestModule/1.0.0": {
                                    "dependencies": {
                                      "PowerForge": "1.0.0"
                                    },
                                    "runtime": {
                                      "TestModule.dll": {}
                                    }
                                  },
                                  "PowerForge/1.0.0": {
                                    "dependencies": {
                                      "Microsoft.PowerShell.SDK": "7.4.1",
                                      "System.IO.Packaging": "10.0.1"
                                    }
                                  },
                                  "Microsoft.PowerShell.SDK/7.4.1": {
                                    "dependencies": {
                                      "Microsoft.Windows.Compatibility": "8.0.1",
                                      "System.Management.Automation": "7.4.1"
                                    }
                                  },
                                  "Microsoft.Windows.Compatibility/8.0.1": {
                                    "dependencies": {
                                      "System.Speech": "8.0.0",
                                      "System.IO.Packaging": "10.0.1"
                                    }
                                  },
                                  "System.IO.Packaging/10.0.1": {},
                                  "System.Management.Automation/7.4.1": {},
                                  "System.Speech/8.0.0": {}
                                }
                              }
                            }
                            """;

        var method = typeof(ModuleBuilder).GetMethod("ComputeExcludedLibraries", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var doc = JsonDocument.Parse(json);
        var targets = doc.RootElement.GetProperty("targets");
        var excluded = (HashSet<string>)method!.Invoke(null, new object[] { targets, "TestModule", Array.Empty<string>() })!;

        Assert.Contains(excluded, value => string.Equals(value, "Microsoft.PowerShell.SDK/7.4.1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(excluded, value => string.Equals(value, "Microsoft.Windows.Compatibility/8.0.1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(excluded, value => string.Equals(value, "System.Management.Automation/7.4.1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(excluded, value => string.Equals(value, "System.Speech/8.0.0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(excluded, value => string.Equals(value, "PowerForge/1.0.0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(excluded, value => string.Equals(value, "System.IO.Packaging/10.0.1", StringComparison.OrdinalIgnoreCase));
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

            var builder = ModuleBuilderTestDependencies.Create();
            var copyMethod = typeof(ModuleBuilder).GetMethod(
                "CopyPublishOutputBinaries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(copyMethod);

            copyMethod!.Invoke(builder, new object[]
            {
                publishDir,
                targetDir,
                "net472",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null!
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

    [Fact]
    public void CopyPublishOutputBinaries_DoesNotReAddExcludedTopLevelDepsFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(root, "publish");
        var targetDir = Path.Combine(root, "target");

        Directory.CreateDirectory(publishDir);
        Directory.CreateDirectory(targetDir);

        try
        {
            File.WriteAllText(Path.Combine(publishDir, "TestModule.dll"), "module");
            File.WriteAllText(Path.Combine(publishDir, "System.IO.Packaging.dll"), "keep");
            File.WriteAllText(Path.Combine(publishDir, "Microsoft.PowerShell.MarkdownRender.dll"), "exclude");
            File.WriteAllText(Path.Combine(publishDir, "System.Speech.dll"), "exclude");

            var depsPath = Path.Combine(publishDir, "TestModule.deps.json");
            File.WriteAllText(depsPath, """
                                        {
                                          "targets": {
                                            ".NETCoreApp,Version=v10.0": {
                                              "TestModule/1.0.0": {
                                                "dependencies": {
                                                  "PowerForge": "1.0.0"
                                                },
                                                "runtime": {
                                                  "TestModule.dll": {}
                                                }
                                              },
                                              "PowerForge/1.0.0": {
                                                "dependencies": {
                                                  "System.IO.Packaging": "10.0.1",
                                                  "Microsoft.PowerShell.SDK": "7.4.1"
                                                },
                                                "runtime": {
                                                  "lib/net8.0/PowerForge.dll": {}
                                                }
                                              },
                                              "System.IO.Packaging/10.0.1": {
                                                "runtime": {
                                                  "lib/net8.0/System.IO.Packaging.dll": {}
                                                }
                                              },
                                              "Microsoft.PowerShell.SDK/7.4.1": {
                                                "dependencies": {
                                                  "Microsoft.PowerShell.MarkdownRender": "7.2.1",
                                                  "System.Speech": "8.0.0"
                                                }
                                              },
                                              "Microsoft.PowerShell.MarkdownRender/7.2.1": {
                                                "runtime": {
                                                  "lib/net8.0/Microsoft.PowerShell.MarkdownRender.dll": {}
                                                }
                                              },
                                              "System.Speech/8.0.0": {
                                                "runtime": {
                                                  "lib/net8.0/System.Speech.dll": {}
                                                },
                                                "runtimeTargets": {
                                                  "runtimes/win/lib/net8.0/System.Speech.dll": {
                                                    "rid": "win",
                                                    "assetType": "runtime"
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
            Assert.NotNull(copyMethod);

            copyMethod!.Invoke(builder, new object[]
            {
                publishDir,
                targetDir,
                "net10.0",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null!
            });

            Assert.True(File.Exists(Path.Combine(targetDir, "TestModule.dll")));
            Assert.True(File.Exists(Path.Combine(targetDir, "System.IO.Packaging.dll")));
            Assert.False(File.Exists(Path.Combine(targetDir, "Microsoft.PowerShell.MarkdownRender.dll")));
            Assert.False(File.Exists(Path.Combine(targetDir, "System.Speech.dll")));
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

    [Fact]
    public void BuildInPlace_WarnsWhenUsingExistingLibPayloadWithoutCsproj()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "TestModule";
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psd1"), "@{ RootModule = 'TestModule.psm1'; ModuleVersion = '1.0.0' }");

            var libDefault = Directory.CreateDirectory(Path.Combine(root, "Lib", "Default"));
            File.WriteAllText(Path.Combine(libDefault.FullName, "Existing.Binary.dll"), "placeholder");

            var logger = new CollectingLogger();
            var builder = ModuleBuilderTestDependencies.Create(logger);
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "1.0.0",
                CsprojPath = string.Empty
            });

            Assert.Contains(logger.Warnings, warning => warning.Contains("using the existing Lib payload without rebuilding binaries", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void CopyPublishOutputBinaries_SkipsRuntimeTargets_WhenDoNotCopyLibrariesRecursively()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(root, "publish");
        var targetDir = Path.Combine(root, "target");

        Directory.CreateDirectory(Path.Combine(publishDir, "runtimes", "win", "native"));
        Directory.CreateDirectory(targetDir);

        try
        {
            File.WriteAllText(Path.Combine(publishDir, "TestModule.dll"), "a");
            File.WriteAllText(Path.Combine(publishDir, "runtimes", "win", "native", "helper.dll"), "b");

            var depsPath = Path.Combine(publishDir, "TestModule.deps.json");
            File.WriteAllText(depsPath, """
                                        {
                                          "targets": {
                                            ".NETCoreApp,Version=v10.0": {
                                              "TestModule/1.0.0": {
                                                "runtime": {
                                                  "TestModule.dll": {}
                                                },
                                                "runtimeTargets": {
                                                  "runtimes/win/native/helper.dll": {
                                                    "rid": "win",
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
                new object[] { Array.Empty<string>(), true });

            copyMethod!.Invoke(builder, new object[]
            {
                publishDir,
                targetDir,
                "net10.0",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                options!
            });

            Assert.True(File.Exists(Path.Combine(targetDir, "TestModule.dll")));
            Assert.False(File.Exists(Path.Combine(targetDir, "runtimes", "win", "native", "helper.dll")));
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

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();
        public bool IsVerbose => false;
        public void Info(string message) { }
        public void Success(string message) { }
        public void Warn(string message) => Warnings.Add(message ?? string.Empty);
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
