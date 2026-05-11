using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
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
    public void WriteManifests_IncludesInstallerAndStorePackageOutputs()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestJson = Path.Combine(root, "Artifacts", "DotNetPublish", "manifest.json");
            var manifestTxt = Path.Combine(root, "Artifacts", "DotNetPublish", "manifest.txt");
            var checksums = Path.Combine(root, "Artifacts", "DotNetPublish", "SHA256SUMS.txt");
            var msiPath = Path.Combine(root, "Artifacts", "Msi", "app", "output", "App.msi");
            var msiSidecarPath = Path.Combine(root, "Artifacts", "Msi", "app", "symbols", "App-symbols.msi");
            var msixPath = Path.Combine(root, "Artifacts", "Store", "app", "App.msixbundle");
            var uploadPath = Path.Combine(root, "Artifacts", "Store", "app", "App.msixupload");
            Directory.CreateDirectory(Path.GetDirectoryName(msiPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(msiSidecarPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(msixPath)!);
            File.WriteAllText(msiPath, "msi");
            File.WriteAllText(msiSidecarPath, "symbols");
            File.WriteAllText(msixPath, "msix");
            File.WriteAllText(uploadPath, "upload");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs
                {
                    ManifestJsonPath = manifestJson,
                    ManifestTextPath = manifestTxt,
                    ChecksumsPath = checksums
                }
            };
            var msiBuilds = new List<DotNetPublishMsiBuildResult>
            {
                new()
                {
                    InstallerId = "app.msi",
                    Target = "app",
                    Framework = "net8.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.Portable,
                    OutputFiles = new[] { msiPath, msiSidecarPath },
                    SignedFiles = new[] { msiPath },
                    Version = "1.2.3"
                }
            };
            var storePackages = new List<DotNetPublishStorePackageResult>
            {
                new()
                {
                    StorePackageId = "app.store",
                    Target = "app",
                    Framework = "net8.0-windows10.0.19041.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.FrameworkDependent,
                    OutputDir = Path.GetDirectoryName(msixPath)!,
                    OutputFiles = new[] { msixPath },
                    UploadFiles = new[] { uploadPath }
                }
            };

            InvokeWriteManifests(plan, new List<DotNetPublishArtefactResult>(), storePackages, msiBuilds);

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestJson));
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            var json = File.ReadAllText(manifestJson);
            Assert.Contains("\"InstallerId\": \"app.msi\"", json, StringComparison.Ordinal);
            Assert.Contains("\"StorePackageId\": \"app.store\"", json, StringComparison.Ordinal);
            Assert.Contains("App.msi", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("App-symbols.msi", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("App.msixupload", json, StringComparison.OrdinalIgnoreCase);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("OutputFiles", out var outputFiles))
                    continue;

                foreach (var file in outputFiles.EnumerateArray())
                {
                    var value = file.GetString();
                    Assert.False(
                        !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value),
                        $"Manifest OutputFiles should be project-relative, but found '{value}'.");
                }
            }

            var text = File.ReadAllText(manifestTxt);
            Assert.Contains("MSI app.msi", text, StringComparison.Ordinal);
            Assert.DoesNotContain("->  (", text, StringComparison.Ordinal);
            Assert.Contains("(2 files version=1.2.3)", text, StringComparison.Ordinal);
            Assert.Contains("Store app.store", text, StringComparison.Ordinal);

            var checksumText = File.ReadAllText(checksums);
            Assert.Contains("App.msi", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("App-symbols.msi", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("App.msixbundle", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("App.msixupload", checksumText, StringComparison.OrdinalIgnoreCase);
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
            var method = GetTrySignOutputMethod();

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
    public void TrySignOutput_DefaultsToExecutablesOnly()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "app.exe"), "dummy");
            File.WriteAllText(Path.Combine(outputDir, "lib.dll"), "dummy");

            var logger = new CollectingLogger();
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Skip
            };

            var runner = new DotNetPublishPipelineRunner(logger);
            var method = GetTrySignOutputMethod();

            _ = method!.Invoke(runner, new object[] { outputDir, sign });
            Assert.Contains(logger.InfoMessages, message => message.Contains("Signing 1 file(s)", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_IncludeDllsSignsExecutablesAndLibraries()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "app.exe"), "dummy");
            File.WriteAllText(Path.Combine(outputDir, "lib.dll"), "dummy");

            var logger = new CollectingLogger();
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                IncludeDlls = true,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Skip
            };

            var runner = new DotNetPublishPipelineRunner(logger);
            var method = GetTrySignOutputMethod();

            _ = method!.Invoke(runner, new object[] { outputDir, sign });
            Assert.Contains(logger.InfoMessages, message => message.Contains("Signing 2 file(s)", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_WhenDllOnlyAndIncludeDllsDisabled_HonorsFailurePolicy()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "lib.dll"), "dummy");

            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var method = GetTrySignOutputMethod();

            var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(runner, new object[] { outputDir, sign }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("no matching files were found", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("IncludeDlls=true", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPublishMsBuildProperties_MergesGlobalTargetAndStyleOverrides()
    {
        var plan = new DotNetPublishPlan
        {
            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PublishSingleFile"] = "true",
                ["WarningsNotAsErrors"] = "IL3000"
            }
        };

        var target = new DotNetPublishTargetPlan
        {
            Name = "app",
            ProjectPath = "App.csproj",
            Publish = new DotNetPublishPublishOptions
            {
                MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PublishSingleFile"] = "false",
                    ["SkipChatServiceSidecarBuild"] = "true"
                },
                StyleOverrides = new Dictionary<string, DotNetPublishStyleOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PortableCompat"] = new DotNetPublishStyleOverride
                    {
                        MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["WarningsNotAsErrors"] = "NU1510",
                            ["WindowsAppSDKSelfContained"] = "true"
                        }
                    }
                }
            }
        };

        var merged = DotNetPublishPipelineRunner.BuildPublishMsBuildProperties(
            plan,
            target,
            DotNetPublishStyle.PortableCompat);

        Assert.Equal("false", merged["PublishSingleFile"]);
        Assert.Equal("true", merged["SkipChatServiceSidecarBuild"]);
        Assert.Equal("NU1510", merged["WarningsNotAsErrors"]);
        Assert.Equal("true", merged["WindowsAppSDKSelfContained"]);
    }

    [Fact]
    public void BuildPublishArguments_AppendsMergedOverridesAfterStyleDefaults()
    {
        var plan = new DotNetPublishPlan
        {
            Configuration = "Release",
            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PublishSingleFile"] = "false"
            }
        };

        var target = new DotNetPublishTargetPlan
        {
            Name = "app",
            ProjectPath = "App.csproj",
            Publish = new DotNetPublishPublishOptions()
        };

        var args = DotNetPublishPipelineRunner.BuildPublishArguments(
            plan,
            target,
            "net8.0-windows10.0.26100.0",
            "win-x64",
            DotNetPublishStyle.PortableCompat,
            "out");

        var firstSingleFile = args.IndexOf("/p:PublishSingleFile=true");
        var finalSingleFile = args.LastIndexOf("/p:PublishSingleFile=false");

        Assert.True(firstSingleFile >= 0, "Expected style defaults to request single-file publish.");
        Assert.True(finalSingleFile > firstSingleFile, "Expected merged overrides to win by appearing after style defaults.");
    }

    [Fact]
    public void BuildWixHarvestFragment_PreservesSubdirectoriesForNestedFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var stagingDir = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            Directory.CreateDirectory(Path.Combine(stagingDir, "af-ZA"));
            Directory.CreateDirectory(Path.Combine(stagingDir, "am-ET"));
            File.WriteAllText(Path.Combine(stagingDir, "af-ZA", "Microsoft.ui.xaml.dll.mui"), "a");
            File.WriteAllText(Path.Combine(stagingDir, "am-ET", "Microsoft.ui.xaml.dll.mui"), "b");
            File.WriteAllText(Path.Combine(stagingDir, "root.txt"), "root");

            var fragment = DotNetPublishPipelineRunner.BuildWixHarvestFragment(
                stagingDir,
                "INSTALLFOLDER",
                "ProductFiles");
            var normalizedFragment = fragment.Replace('\\', '/');
            var expectedAfZaPath = Path.Combine(stagingDir, "af-ZA", "Microsoft.ui.xaml.dll.mui").Replace('\\', '/');
            var expectedAmEtPath = Path.Combine(stagingDir, "am-ET", "Microsoft.ui.xaml.dll.mui").Replace('\\', '/');
            var expectedRootPath = Path.Combine(stagingDir, "root.txt").Replace('\\', '/');

            Assert.Contains("<DirectoryRef Id=\"INSTALLFOLDER\">", fragment, StringComparison.Ordinal);
            Assert.Contains("Name=\"af-ZA\"", fragment, StringComparison.Ordinal);
            Assert.Contains("Name=\"am-ET\"", fragment, StringComparison.Ordinal);
            Assert.Contains(expectedAfZaPath, normalizedFragment, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedAmEtPath, normalizedFragment, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedRootPath, normalizedFragment, StringComparison.OrdinalIgnoreCase);
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

    private static MethodInfo GetTrySignOutputMethod()
    {
        var method = typeof(DotNetPublishPipelineRunner).GetMethod(
            "TrySignOutput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "TrySignOutput private method not found. Was it renamed or made public?");
        return method!;
    }

    private static (string? ManifestJson, string? ManifestText, string? ChecksumsPath) InvokeWriteManifests(
        DotNetPublishPlan plan,
        List<DotNetPublishArtefactResult> artefacts,
        List<DotNetPublishStorePackageResult> storePackages,
        List<DotNetPublishMsiBuildResult> msiBuilds)
    {
        var method = typeof(DotNetPublishPipelineRunner).GetMethod(
            "WriteManifests",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var raw = method!.Invoke(null, new object?[] { plan, artefacts, storePackages, msiBuilds });
        Assert.NotNull(raw);
        return Assert.IsType<(string? ManifestJson, string? ManifestText, string? ChecksumsPath)>(raw);
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

    private sealed class CollectingLogger : ILogger
    {
        public List<string> InfoMessages { get; } = new();
        public bool IsVerbose => false;
        public void Info(string message) => InfoMessages.Add(message);
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
