using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerProfileTests
{
    [Fact]
    public void Plan_AppliesDefaultProfileTargetFilter()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var svc = CreateProject(root, "Svc/Svc.csproj");

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
                    NewTarget("app", app),
                    NewTarget("svc", svc)
                },
                Profiles = new[]
                {
                    new DotNetPublishProfile
                    {
                        Name = "service-only",
                        Default = true,
                        Targets = new[] { "svc" }
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var plan = runner.Plan(spec, null);

            var target = Assert.Single(plan.Targets);
            Assert.Equal("svc", target.Name);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenRequestedProfileMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");

            var spec = new DotNetPublishSpec
            {
                Profile = "missing",
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[] { NewTarget("app", app) },
                Profiles = new[]
                {
                    new DotNetPublishProfile { Name = "release", Default = true }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("Profile", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AppliesProfileRuntimeFrameworkAndStyleOverrides()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");

            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[] { NewTarget("app", app) },
                Profiles = new[]
                {
                    new DotNetPublishProfile
                    {
                        Name = "aot",
                        Default = true,
                        Runtimes = new[] { "win-arm64" },
                        Frameworks = new[] { "net10.0-windows" },
                        Style = DotNetPublishStyle.AotSpeed
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var plan = runner.Plan(spec, null);

            Assert.Equal(new[] { "win-arm64" }, plan.Targets[0].Publish.Runtimes);
            Assert.Equal(new[] { "net10.0-windows" }, plan.Targets[0].Publish.Frameworks);
            Assert.Equal("net10.0-windows", plan.Targets[0].Publish.Framework);
            Assert.Equal(DotNetPublishStyle.AotSpeed, plan.Targets[0].Publish.Style);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishTarget NewTarget(string name, string projectPath)
    {
        return new DotNetPublishTarget
        {
            Name = name,
            ProjectPath = projectPath,
            Publish = new DotNetPublishPublishOptions
            {
                Framework = "net10.0",
                Runtimes = new[] { "win-x64" },
                UseStaging = false
            }
        };
    }

    private static string CreateProject(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
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
            // best effort
        }
    }
}

