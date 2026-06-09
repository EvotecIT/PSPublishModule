using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerServicePackageTests
{
    [Fact]
    public void Plan_PreservesServicePackageOptions()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "svc",
                        ProjectPath = csproj,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false,
                            Service = new DotNetPublishServicePackageOptions
                            {
                                ServiceName = "PowerForge.Svc",
                                DisplayName = "PowerForge Service",
                                Description = "Service package test",
                                GenerateInstallScript = true,
                                GenerateUninstallScript = false,
                                GenerateRunOnceScript = true,
                                Recovery = new DotNetPublishServiceRecoveryOptions
                                {
                                    Enabled = true,
                                    ResetPeriodSeconds = 120,
                                    RestartDelaySeconds = 10
                                }
                            }
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var service = Assert.Single(plan.Targets).Publish.Service;
            Assert.NotNull(service);
            Assert.Equal("PowerForge.Svc", service!.ServiceName);
            Assert.Equal("PowerForge Service", service.DisplayName);
            Assert.True(service.GenerateInstallScript);
            Assert.False(service.GenerateUninstallScript);
            Assert.True(service.GenerateRunOnceScript);
            Assert.NotNull(service.Recovery);
            Assert.True(service.Recovery!.Enabled);
            Assert.Equal(120, service.Recovery.ResetPeriodSeconds);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TryCreateServicePackage_GeneratesScriptsAndMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");

            var options = new DotNetPublishServicePackageOptions
            {
                ServiceName = "PowerForge.Svc",
                DisplayName = "PowerForge Service",
                Description = "Generated service package",
                GenerateInstallScript = true,
                GenerateUninstallScript = true,
                GenerateRunOnceScript = true,
                Recovery = new DotNetPublishServiceRecoveryOptions
                {
                    Enabled = true,
                    ResetPeriodSeconds = 900,
                    RestartDelaySeconds = 30
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TryCreateServicePackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(runner, new object[] { outputDir, "svc", "win-x64", options }) as DotNetPublishServicePackageResult;
            Assert.NotNull(result);
            Assert.Equal("PowerForge.Svc", result!.ServiceName);
            Assert.True(File.Exists(result.InstallScriptPath));
            Assert.True(File.Exists(result.UninstallScriptPath));
            Assert.True(File.Exists(result.RunOnceScriptPath));
            Assert.True(File.Exists(result.MetadataPath));

            var installContent = File.ReadAllText(result.InstallScriptPath!);
            Assert.Contains("PowerForge.Svc", installContent, StringComparison.Ordinal);
            Assert.Contains("Svc.exe", installContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", installContent, StringComparison.Ordinal);

            var uninstallContent = File.ReadAllText(result.UninstallScriptPath!);
            Assert.DoesNotContain("{{", uninstallContent, StringComparison.Ordinal);

            var metadata = File.ReadAllText(result.MetadataPath);
            Assert.Contains("\"ServiceName\": \"PowerForge.Svc\"", metadata, StringComparison.Ordinal);
            Assert.Contains("\"InstallScriptPath\":", metadata, StringComparison.Ordinal);
            Assert.Contains("\"Recovery\":", metadata, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TryCreateServicePackage_ThrowsWhenExecutableEscapesOutputDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var outsideDir = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            var outsideExe = Path.Combine(outsideDir, "Svc.exe");
            File.WriteAllText(outsideExe, "dummy");

            var options = new DotNetPublishServicePackageOptions
            {
                ServiceName = "PowerForge.Svc",
                ExecutablePath = outsideExe,
                GenerateInstallScript = true,
                GenerateUninstallScript = false
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TryCreateServicePackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(runner, new object[] { outputDir, "svc", "win-x64", options }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("outside", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AddsServiceLifecycleStep_WhenEnabled()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "svc",
                        ProjectPath = csproj,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false,
                            Service = new DotNetPublishServicePackageOptions
                            {
                                ServiceName = "PowerForge.Svc",
                                Lifecycle = new DotNetPublishServiceLifecycleOptions
                                {
                                    Enabled = true,
                                    WhatIf = true
                                }
                            }
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var kinds = plan.Steps.Select(s => s.Kind).ToArray();
            Assert.Contains(DotNetPublishStepKind.Publish, kinds);
            Assert.Contains(DotNetPublishStepKind.ServiceLifecycle, kinds);

            var publishIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Publish);
            var lifecycleIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.ServiceLifecycle);
            Assert.True(publishIndex >= 0);
            Assert.True(lifecycleIndex > publishIndex);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_DoesNotAddServiceLifecycleStep_WhenInlineRebuildMode()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "Svc/Svc.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "svc",
                        ProjectPath = csproj,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false,
                            Service = new DotNetPublishServicePackageOptions
                            {
                                ServiceName = "PowerForge.Svc",
                                Lifecycle = new DotNetPublishServiceLifecycleOptions
                                {
                                    Enabled = true,
                                    Mode = DotNetPublishServiceLifecycleMode.InlineRebuild,
                                    WhatIf = true
                                }
                            }
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            Assert.DoesNotContain(plan.Steps, s => s.Kind == DotNetPublishStepKind.ServiceLifecycle);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ExecuteServiceLifecycle_WhatIf_DoesNotThrow()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var exePath = Path.Combine(outputDir, "Svc.exe");
            File.WriteAllText(exePath, "dummy");

            var package = new DotNetPublishServicePackageResult
            {
                ServiceName = "PowerForge.Svc",
                DisplayName = "PowerForge Service",
                Description = "Service lifecycle whatif test",
                ExecutablePath = "Svc.exe",
                MetadataPath = Path.Combine(outputDir, "ServicePackage.json")
            };

            var lifecycle = new DotNetPublishServiceLifecycleOptions
            {
                Enabled = true,
                WhatIf = true
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("ExecuteServiceLifecycle", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(runner, new object[] { outputDir, package, lifecycle });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ExecuteServiceLifecycleInlineBeforePublish_WhatIf_DoesNotThrow()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var service = new DotNetPublishServicePackageOptions
            {
                ServiceName = "PowerForge.Svc",
                Lifecycle = new DotNetPublishServiceLifecycleOptions
                {
                    Enabled = true,
                    Mode = DotNetPublishServiceLifecycleMode.InlineRebuild,
                    WhatIf = true
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("ExecuteServiceLifecycleInlineBeforePublish", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(runner, new object[] { outputDir, "svc", service, service.Lifecycle! });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ExecuteServiceLifecycleInlineAfterPublish_WhatIf_DoesNotThrow()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");

            var package = new DotNetPublishServicePackageResult
            {
                ServiceName = "PowerForge.Svc",
                DisplayName = "PowerForge Service",
                Description = "Service lifecycle inline-post whatif test",
                ExecutablePath = "Svc.exe",
                MetadataPath = Path.Combine(outputDir, "ServicePackage.json")
            };
            var lifecycle = new DotNetPublishServiceLifecycleOptions
            {
                Enabled = true,
                Mode = DotNetPublishServiceLifecycleMode.InlineRebuild,
                WhatIf = true
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("ExecuteServiceLifecycleInlineAfterPublish", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(runner, new object[] { outputDir, package, lifecycle });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TryCreateServicePackage_ConfigBootstrap_CopiesWhenMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");
            File.WriteAllText(Path.Combine(outputDir, "appsettings.example.json"), "{ \"mode\": \"example\" }");

            var options = new DotNetPublishServicePackageOptions
            {
                ServiceName = "PowerForge.Svc",
                ConfigBootstrap = new[]
                {
                    new DotNetPublishConfigBootstrapRule
                    {
                        SourcePath = "appsettings.example.json",
                        DestinationPath = "appsettings.json"
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TryCreateServicePackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(runner, new object[] { outputDir, "svc", "win-x64", options }) as DotNetPublishServicePackageResult;
            Assert.NotNull(result);

            var destination = Path.Combine(outputDir, "appsettings.json");
            Assert.True(File.Exists(destination));
            Assert.Equal("{ \"mode\": \"example\" }", File.ReadAllText(destination));
            Assert.Contains("appsettings.json", result!.ConfigBootstrapFiles, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TryCreateServicePackage_ConfigBootstrap_DoesNotOverwriteByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");
            File.WriteAllText(Path.Combine(outputDir, "appsettings.example.json"), "{ \"mode\": \"example\" }");
            File.WriteAllText(Path.Combine(outputDir, "appsettings.json"), "{ \"mode\": \"existing\" }");

            var options = new DotNetPublishServicePackageOptions
            {
                ServiceName = "PowerForge.Svc",
                ConfigBootstrap = new[]
                {
                    new DotNetPublishConfigBootstrapRule
                    {
                        SourcePath = "appsettings.example.json",
                        DestinationPath = "appsettings.json"
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TryCreateServicePackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(runner, new object[] { outputDir, "svc", "win-x64", options }) as DotNetPublishServicePackageResult;
            Assert.NotNull(result);

            Assert.Equal("{ \"mode\": \"existing\" }", File.ReadAllText(Path.Combine(outputDir, "appsettings.json")));
            Assert.Empty(result!.ConfigBootstrapFiles);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TryCreateServicePackage_ConfigBootstrap_MissingSourceFails_WhenPolicyFail()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "Svc.exe"), "dummy");

            var options = new DotNetPublishServicePackageOptions
            {
                ServiceName = "PowerForge.Svc",
                ConfigBootstrap = new[]
                {
                    new DotNetPublishConfigBootstrapRule
                    {
                        SourcePath = "missing.example.json",
                        DestinationPath = "appsettings.json",
                        OnMissingSource = DotNetPublishPolicyMode.Fail
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TryCreateServicePackage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(runner, new object[] { outputDir, "svc", "win-x64", options }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("source file not found", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RenderServiceTemplate_ThrowsWhenTokenMissing()
    {
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("RenderServiceTemplate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var ex = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(
                null,
                new object[]
                {
                    "Test.ps1",
                    "Name={{ServiceName}} Missing={{UnknownToken}}",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ServiceName"] = "svc"
                    }
                }));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("missing token", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateProject(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return fullPath;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
