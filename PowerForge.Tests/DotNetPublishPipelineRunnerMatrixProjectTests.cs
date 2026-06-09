using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerMatrixProjectTests
{
    [Fact]
    public void Plan_ResolvesProjectPathFromProjectCatalog()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = CreateProject(root, "src/app.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Projects = new[]
                {
                    new DotNetPublishProject { Id = "app", Path = "src/app.csproj" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "app",
                        ProjectId = "app",
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            Assert.Single(plan.Targets);
            Assert.Equal(Path.GetFullPath(projectPath), plan.Targets[0].ProjectPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AppliesMatrixIncludeExcludeFilters_WithStyleDimension()
    {
        var root = CreateTempRoot();
        try
        {
            _ = CreateProject(root, "src/app.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false
                },
                Projects = new[]
                {
                    new DotNetPublishProject { Id = "app", Path = "src/app.csproj" }
                },
                Matrix = new DotNetPublishMatrix
                {
                    Include = new[]
                    {
                        new DotNetPublishMatrixRule { Framework = "net10.*" }
                    },
                    Exclude = new[]
                    {
                        new DotNetPublishMatrixRule { Runtime = "linux-*", Style = "Aot*" }
                    }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "app",
                        ProjectId = "app",
                        Publish = new DotNetPublishPublishOptions
                        {
                            Frameworks = new[] { "net10.0", "net8.0" },
                            Runtimes = new[] { "win-x64", "linux-x64" },
                            Styles = new[] { DotNetPublishStyle.Portable, DotNetPublishStyle.AotSpeed },
                            UseStaging = false
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var target = Assert.Single(plan.Targets);

            Assert.Equal(3, target.Combinations.Length);
            Assert.All(target.Combinations, c => Assert.Equal("net10.0", c.Framework));
            Assert.DoesNotContain(target.Combinations, c => c.Runtime == "linux-x64" && c.Style == DotNetPublishStyle.AotSpeed);

            var publishSteps = plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.Publish).ToArray();
            Assert.Equal(3, publishSteps.Length);
            Assert.Contains(publishSteps, s => s.Style == DotNetPublishStyle.AotSpeed);
            Assert.Contains(publishSteps, s => s.Style == DotNetPublishStyle.Portable);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenProjectIdIsUnknown()
    {
        var root = CreateTempRoot();
        try
        {
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
                        Name = "app",
                        ProjectId = "missing",
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            UseStaging = false
                        }
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("ProjectId", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
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

