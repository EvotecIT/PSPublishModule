using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerHardeningTests
{
    [Fact]
    public void Plan_DeniesOutputPathOutsideProjectRoot_ByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProjectFile(root, "App.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Targets[0].Publish.OutputPath = "..\\outside\\{target}";

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec, null));
            Assert.Contains("outside ProjectRoot", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AllowsOutputPathOutsideProjectRoot_WhenEnabled()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProjectFile(root, "App.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.DotNet.AllowOutputOutsideProjectRoot = true;
            spec.Targets[0].Publish.OutputPath = "..\\outside\\{target}";

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var plan = runner.Plan(spec, null);
            Assert.NotNull(plan);
            Assert.True(plan.AllowOutputOutsideProjectRoot);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_DeniesManifestPathOutsideProjectRoot_ByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var csproj = CreateProjectFile(root, "App.csproj");
            var spec = CreateBaseSpec(root, csproj);
            spec.Outputs.ManifestJsonPath = "..\\outside\\manifest.json";

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec, null));
            Assert.Contains("outside ProjectRoot", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_ManifestStep_WritesChecksumsWhenConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestJson = Path.Combine(root, "Artifacts", "DotNetPublish", "manifest.json");
            var manifestTxt = Path.Combine(root, "Artifacts", "DotNetPublish", "manifest.txt");
            var checksums = Path.Combine(root, "Artifacts", "DotNetPublish", "SHA256SUMS.txt");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs
                {
                    ManifestJsonPath = manifestJson,
                    ManifestTextPath = manifestTxt,
                    ChecksumsPath = checksums
                },
                Steps = new[]
                {
                    new DotNetPublishStep
                    {
                        Key = "manifest",
                        Kind = DotNetPublishStepKind.Manifest,
                        Title = "Write manifest"
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(checksums, result.ChecksumsPath);
            Assert.True(File.Exists(checksums));
            Assert.True(File.Exists(manifestJson));
            Assert.True(File.Exists(manifestTxt));

            var lines = File.ReadAllLines(checksums);
            Assert.True(lines.Length >= 2);
            Assert.Contains(lines, l => l.Contains("manifest.json", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, l => l.Contains("manifest.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_WhenMissingToolAndPolicyFail_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "app.exe"), "dummy");

            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                ToolPath = "definitely-not-a-real-signtool.exe",
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TrySignOutput", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(runner, new object[] { outputDir, sign }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.True(
                ex.InnerException!.Message.Contains("Signing requested", StringComparison.OrdinalIgnoreCase)
                || ex.InnerException!.Message.Contains("Signing failed", StringComparison.OrdinalIgnoreCase),
                $"Unexpected message: {ex.InnerException!.Message}");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void EnsureOutputDirectoryUnlocked_WhenLockedAndPolicyFail_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var filePath = Path.Combine(outputDir, "app.dll");
            File.WriteAllText(filePath, "dummy");

            using var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                LockedOutputGuard = true,
                OnLockedOutput = DotNetPublishPolicyMode.Fail,
                LockedOutputSampleLimit = 5
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("EnsureOutputDirectoryUnlocked", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var ex = Assert.Throws<TargetInvocationException>(() =>
                method!.Invoke(runner, new object?[] { plan, outputDir, "test-context", "Test.Service" }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("locked", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void EnsureOutputDirectoryUnlocked_WhenLockedAndPolicyWarn_DoesNotThrow()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var filePath = Path.Combine(outputDir, "app.dll");
            File.WriteAllText(filePath, "dummy");

            using var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                LockedOutputGuard = true,
                OnLockedOutput = DotNetPublishPolicyMode.Warn,
                LockedOutputSampleLimit = 5
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("EnsureOutputDirectoryUnlocked", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(runner, new object?[] { plan, outputDir, "test-context", "Test.Service" });
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishSpec CreateBaseSpec(string root, string csprojPath)
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
                    ProjectPath = csprojPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        UseStaging = false,
                        Zip = false
                    }
                }
            }
        };
    }

    private static string CreateProjectFile(string root, string fileName)
    {
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, fileName);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return projectPath;
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
