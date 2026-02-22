using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerStatePreserveTests
{
    [Fact]
    public void Plan_ThrowsWhenStateEnabledWithoutRules()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Targets[0].Publish.State = new DotNetPublishStatePreservationOptions
            {
                Enabled = true
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("State", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_NormalizesStateRules_WhenEnabled()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Targets[0].Publish.State = new DotNetPublishStatePreservationOptions
            {
                Enabled = true,
                StoragePath = " Artifacts/State/{target} ",
                Rules = new[]
                {
                    new DotNetPublishStateRule
                    {
                        SourcePath = " appsettings.json ",
                        DestinationPath = " config/appsettings.json ",
                        Overwrite = false
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var state = Assert.Single(plan.Targets).Publish.State;
            Assert.NotNull(state);
            Assert.True(state!.Enabled);
            Assert.Equal("Artifacts/State/{target}", state.StoragePath);
            var rule = Assert.Single(state.Rules);
            Assert.Equal("appsettings.json", rule.SourcePath);
            Assert.Equal("config/appsettings.json", rule.DestinationPath);
            Assert.False(rule.Overwrite);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PreserveAndRestoreState_RespectsOverwriteRules()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "out");
            Directory.CreateDirectory(Path.Combine(outputDir, "Data"));
            File.WriteAllText(Path.Combine(outputDir, "appsettings.json"), "old");
            File.WriteAllText(Path.Combine(outputDir, "Data", "state.db"), "state");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false
            };

            var state = new DotNetPublishStatePreservationOptions
            {
                Enabled = true,
                Rules = new[]
                {
                    new DotNetPublishStateRule
                    {
                        SourcePath = "appsettings.json",
                        Overwrite = false
                    },
                    new DotNetPublishStateRule
                    {
                        SourcePath = "Data",
                        DestinationPath = "Data",
                        Overwrite = true
                    }
                }
            };

            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["target"] = "app",
                ["rid"] = "win-x64",
                ["framework"] = "net10.0",
                ["style"] = DotNetPublishStyle.Portable.ToString(),
                ["configuration"] = "Release"
            };

            var preserved = InvokePreserveStateBeforePublish(plan, outputDir, state, tokens, "app");
            Assert.NotNull(preserved);
            Assert.True(Directory.Exists(preserved!.StoragePath));
            Assert.True(preserved.PreservedFiles >= 2);

            Directory.Delete(outputDir, recursive: true);
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "appsettings.json"), "new");

            InvokeRestorePreservedState(outputDir, preserved);
            Assert.Equal("new", File.ReadAllText(Path.Combine(outputDir, "appsettings.json")));
            Assert.Equal("state", File.ReadAllText(Path.Combine(outputDir, "Data", "state.db")));
            Assert.True(preserved.RestoredFiles >= 1);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PreserveState_ThrowsWhenMissingSourceAndPolicyFail()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "out");
            Directory.CreateDirectory(outputDir);

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false
            };
            var state = new DotNetPublishStatePreservationOptions
            {
                Enabled = true,
                OnMissingSource = DotNetPublishPolicyMode.Fail,
                Rules = new[]
                {
                    new DotNetPublishStateRule
                    {
                        SourcePath = "missing-license.txlic"
                    }
                }
            };

            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["target"] = "app",
                ["rid"] = "win-x64",
                ["framework"] = "net10.0",
                ["style"] = DotNetPublishStyle.Portable.ToString(),
                ["configuration"] = "Release"
            };

            var ex = Assert.Throws<TargetInvocationException>(
                () => InvokePreserveStateBeforePublish(plan, outputDir, state, tokens, "app"));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("State source was not found", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishStateTransferResult? InvokePreserveStateBeforePublish(
        DotNetPublishPlan plan,
        string outputDir,
        DotNetPublishStatePreservationOptions state,
        IReadOnlyDictionary<string, string> tokens,
        string contextLabel)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("PreserveStateBeforePublish", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(runner, new object?[] { plan, outputDir, state, tokens, contextLabel }) as DotNetPublishStateTransferResult;
    }

    private static void InvokeRestorePreservedState(string outputDir, DotNetPublishStateTransferResult state)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("RestorePreservedState", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(runner, new object?[] { outputDir, state });
    }

    private static DotNetPublishSpec CreateBaseSpec(string root, string projectPath)
    {
        return new DotNetPublishSpec
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
                    ProjectPath = projectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        UseStaging = false
                    }
                }
            }
        };
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
