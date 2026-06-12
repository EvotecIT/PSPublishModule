using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerEnvironmentTests
{
    [Fact]
    public void Plan_ResolvesDotNetEnvironmentVariableFromFallbackList()
    {
        var root = CreateTempRoot();
        var sourceName = "POWERFORGE_TEST_SOURCE_TOKEN_" + Guid.NewGuid().ToString("N");
        try
        {
            var projectPath = CreateProject(root);
            Environment.SetEnvironmentVariable(sourceName, "token-value");

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                new DotNetPublishSpec
                {
                    DotNet = new DotNetPublishDotNetOptions
                    {
                        ProjectRoot = root,
                        Restore = false,
                        Build = false,
                        Runtimes = new[] { "win-x64" },
                        EnvironmentVariables = new Dictionary<string, DotNetPublishEnvironmentVariable>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["LICENSING_PACKAGES_TOKEN"] = new()
                            {
                                FromEnvironmentVariables = new[] { "MISSING_TOKEN", sourceName },
                                Required = true
                            }
                        }
                    },
                    Targets = new[] { NewTarget(projectPath) }
                },
                configPath: null);

            Assert.Equal("token-value", plan.EnvironmentVariables["LICENSING_PACKAGES_TOKEN"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(sourceName, null);
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenRequiredDotNetEnvironmentVariableIsMissing()
    {
        var root = CreateTempRoot();
        var sourceName = "POWERFORGE_TEST_MISSING_TOKEN_" + Guid.NewGuid().ToString("N");
        try
        {
            var projectPath = CreateProject(root);
            Environment.SetEnvironmentVariable(sourceName, null);

            var ex = Assert.Throws<InvalidOperationException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                new DotNetPublishSpec
                {
                    DotNet = new DotNetPublishDotNetOptions
                    {
                        ProjectRoot = root,
                        Restore = false,
                        Build = false,
                        Runtimes = new[] { "win-x64" },
                        EnvironmentVariables = new Dictionary<string, DotNetPublishEnvironmentVariable>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["LICENSING_PACKAGES_TOKEN"] = new()
                            {
                                FromEnvironmentVariables = new[] { sourceName },
                                Required = true
                            }
                        }
                    },
                    Targets = new[] { NewTarget(projectPath) }
                },
                configPath: null));

            Assert.Contains("LICENSING_PACKAGES_TOKEN", ex.Message, StringComparison.Ordinal);
            Assert.Contains(sourceName, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AllowsMissingRequiredDotNetEnvironmentVariableWhenNotEnforced()
    {
        var root = CreateTempRoot();
        var sourceName = "POWERFORGE_TEST_PLAN_ONLY_TOKEN_" + Guid.NewGuid().ToString("N");
        try
        {
            var projectPath = CreateProject(root);
            Environment.SetEnvironmentVariable(sourceName, null);

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                new DotNetPublishSpec
                {
                    DotNet = new DotNetPublishDotNetOptions
                    {
                        ProjectRoot = root,
                        Restore = false,
                        Build = false,
                        Runtimes = new[] { "win-x64" },
                        EnvironmentVariables = new Dictionary<string, DotNetPublishEnvironmentVariable>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["LICENSING_PACKAGES_TOKEN"] = new()
                            {
                                FromEnvironmentVariables = new[] { sourceName },
                                Required = true
                            }
                        }
                    },
                    Targets = new[] { NewTarget(projectPath) }
                },
                configPath: null,
                enforceRequiredEnvironmentVariables: false);

            Assert.False(plan.EnvironmentVariables.ContainsKey("LICENSING_PACKAGES_TOKEN"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishTarget NewTarget(string projectPath)
        => new()
        {
            Name = "app",
            ProjectPath = projectPath,
            Publish = new DotNetPublishPublishOptions
            {
                Framework = "net10.0",
                Runtimes = new[] { "win-x64" },
                UseStaging = false
            }
        };

    private static string CreateProject(string root)
    {
        var path = Path.Combine(root, "App", "App.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return path;
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
            // Best effort cleanup for transient test roots.
        }
    }
}
