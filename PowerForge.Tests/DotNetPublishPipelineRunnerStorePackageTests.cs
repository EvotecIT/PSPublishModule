using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerStorePackageTests
{
    [Fact]
    public void Plan_AddsStorePackageStep_WhenPackagingProjectPathConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var packaging = CreateProject(root, "Store/Package.wapproj");

            var spec = CreateBaseSpec(root, app);
            spec.StorePackages = new[]
            {
                new DotNetPublishStorePackage
                {
                    Id = "app.store",
                    PrepareFromTarget = "app",
                    PackagingProjectPath = "Store/Package.wapproj"
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var storePackage = Assert.Single(plan.StorePackages);
            var storeStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.StorePackage);

            Assert.Equal(Path.GetFullPath(packaging), Path.GetFullPath(storeStep.StorePackageProjectPath!));
            Assert.Equal("app.store", storePackage.Id);

            var kinds = plan.Steps.Select(s => s.Kind).ToArray();
            var publishIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Publish);
            var storeIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.StorePackage);
            var manifestIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Manifest);

            Assert.True(publishIndex >= 0);
            Assert.True(storeIndex > publishIndex);
            Assert.True(manifestIndex > storeIndex);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveStoreBuildExecutable_UsesDotnetForStandardProjects()
    {
        var root = CreateTempRoot();
        try
        {
            var project = CreateProject(root, "App/App.csproj");

            var executable = DotNetPublishPipelineRunner.ResolveStoreBuildExecutable(project);

            Assert.Equal("dotnet", executable);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveStoreBuildExecutable_UsesMsBuildEnvOverrideForWapProj()
    {
        var root = CreateTempRoot();
        var previous = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
        try
        {
            var project = CreateProject(root, "Store/App.wapproj");
            var fakeMsBuild = Path.Combine(root, "tools", "MSBuild.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeMsBuild)!);
            File.WriteAllText(fakeMsBuild, "fake", new UTF8Encoding(false));
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", fakeMsBuild);

            var executable = DotNetPublishPipelineRunner.ResolveStoreBuildExecutable(project);

            Assert.Equal(Path.GetFullPath(fakeMsBuild), executable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", previous);
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_StorePackageFiltersLimitStepsToMatchingCombination()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var packaging = CreateProject(root, "Store/Package.wapproj");

            var spec = CreateBaseSpec(root, app);
            spec.Targets[0].Publish.Frameworks = new[] { "net8.0", "net10.0" };
            spec.Targets[0].Publish.Runtimes = new[] { "win-x64", "win-arm64" };
            spec.Targets[0].Publish.Styles = new[] { DotNetPublishStyle.FrameworkDependent, DotNetPublishStyle.PortableCompat };
            spec.StorePackages = new[]
            {
                new DotNetPublishStorePackage
                {
                    Id = "app.store",
                    PrepareFromTarget = "app",
                    PackagingProjectPath = packaging,
                    Frameworks = new[] { "net8.0" },
                    Runtimes = new[] { "win-arm64" },
                    Styles = new[] { DotNetPublishStyle.FrameworkDependent }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var storeStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.StorePackage);

            Assert.Equal("net8.0", storeStep.Framework);
            Assert.Equal("win-arm64", storeStep.Runtime);
            Assert.Equal(DotNetPublishStyle.FrameworkDependent, storeStep.Style);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_StorePackage_BuildsOutputsAndWritesChecksums()
    {
        var root = CreateTempRoot();
        try
        {
            var packagingProject = CreateFakeStorePackagingProject(root);
            var outputDir = Path.Combine(root, "Artifacts", "Store", "app.store");
            var manifestJson = Path.Combine(root, "Artifacts", "DotNetPublish", "manifest.json");
            var manifestTxt = Path.Combine(root, "Artifacts", "DotNetPublish", "manifest.txt");
            var checksums = Path.Combine(root, "Artifacts", "DotNetPublish", "SHA256SUMS.txt");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Restore = true,
                Build = false,
                StorePackages = new[]
                {
                    new DotNetPublishStorePackagePlan
                    {
                        Id = "app.store",
                        PrepareFromTarget = "app",
                        PackagingProjectPath = packagingProject,
                        OutputPath = outputDir,
                        BuildMode = DotNetPublishStoreBuildMode.StoreUpload,
                        Bundle = DotNetPublishStoreBundleMode.Always
                    }
                },
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
                        Key = "store.package:app.store:app:net8.0-windows10.0.19041.0:win-x64:FrameworkDependent",
                        Kind = DotNetPublishStepKind.StorePackage,
                        Title = "Store package",
                        StorePackageId = "app.store",
                        TargetName = "app",
                        Framework = "net8.0-windows10.0.19041.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent,
                        StorePackageProjectPath = packagingProject,
                        StorePackageOutputPath = outputDir
                    },
                    new DotNetPublishStep
                    {
                        Key = "manifest",
                        Kind = DotNetPublishStepKind.Manifest,
                        Title = "Write manifest"
                    }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            var store = Assert.Single(result.StorePackages);
            Assert.Single(store.OutputFiles);
            Assert.Single(store.UploadFiles);
            Assert.Single(store.SymbolFiles);
            Assert.True(File.Exists(store.OutputFiles[0]));
            Assert.True(File.Exists(store.UploadFiles[0]));
            Assert.True(File.Exists(store.SymbolFiles[0]));
            Assert.True(File.Exists(checksums));

            var checksumText = File.ReadAllText(checksums);
            Assert.Contains(".msixbundle", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".msixupload", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".appxsym", checksumText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_StorePackage_FallsBackToDefaultAppPackagesDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var packagingProject = CreateFakeStorePackagingProject(root, writeToDefaultAppPackages: true);
            var configuredOutputDir = Path.Combine(root, "Artifacts", "Store", "app.store");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Restore = true,
                Build = false,
                StorePackages = new[]
                {
                    new DotNetPublishStorePackagePlan
                    {
                        Id = "app.store",
                        PrepareFromTarget = "app",
                        PackagingProjectPath = packagingProject,
                        OutputPath = configuredOutputDir,
                        BuildMode = DotNetPublishStoreBuildMode.StoreUpload,
                        Bundle = DotNetPublishStoreBundleMode.Always
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep
                    {
                        Key = "store.package:app.store:app:net8.0-windows10.0.19041.0:win-x64:FrameworkDependent",
                        Kind = DotNetPublishStepKind.StorePackage,
                        Title = "Store package",
                        StorePackageId = "app.store",
                        TargetName = "app",
                        Framework = "net8.0-windows10.0.19041.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent,
                        StorePackageProjectPath = packagingProject,
                        StorePackageOutputPath = configuredOutputDir
                    }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            var store = Assert.Single(result.StorePackages);
            Assert.Single(store.OutputFiles);
            Assert.EndsWith(".msixbundle", store.OutputFiles[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine("Store", "AppPackages"), store.OutputDir, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DetermineStoreOutputDir_UsesCommonParentForMultiRootOutputs()
    {
        var root = CreateTempRoot();
        try
        {
            var commonRoot = Path.Combine(root, "Store", "AppPackages", "Contoso_1.0.0.0_Test");
            var package = Path.Combine(commonRoot, "Contoso.msixbundle");
            var upload = Path.Combine(root, "Store", "AppPackages", "Contoso_1.0.0.0_x64_bundle.msixupload");
            var symbol = Path.Combine(commonRoot, "Contoso.appxsym");

            Directory.CreateDirectory(Path.GetDirectoryName(package)!);
            File.WriteAllText(package, "bundle", new UTF8Encoding(false));
            File.WriteAllText(upload, "upload", new UTF8Encoding(false));
            File.WriteAllText(symbol, "sym", new UTF8Encoding(false));

            var detected = DotNetPublishPipelineRunner.DetermineStoreOutputDir(
                Path.Combine(root, "Artifacts", "Store", "unused"),
                new[] { package },
                new[] { upload },
                new[] { symbol });

            Assert.Equal(Path.Combine(root, "Store", "AppPackages"), detected);
        }
        finally
        {
            TryDelete(root);
        }
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
        File.WriteAllText(fullPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>", new UTF8Encoding(false));
        return fullPath;
    }

    private static string CreateFakeStorePackagingProject(string root, bool writeToDefaultAppPackages = false)
    {
        var path = Path.Combine(root, "Store", "FakeStore.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var outputRootExpression = writeToDefaultAppPackages
            ? "$([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', 'AppPackages'))"
            : "$(AppxPackageDir)";
        File.WriteAllText(path, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <Target Name="PowerForgeFakeStoreOutputs" AfterTargets="Build">
    <PropertyGroup>
      <PowerForgeStoreOutputRoot>OUTPUT_ROOT_TOKEN</PowerForgeStoreOutputRoot>
      <PowerForgeBundlePath>$([System.IO.Path]::Combine('$(PowerForgeStoreOutputRoot)', 'FakeApp.msixbundle'))</PowerForgeBundlePath>
      <PowerForgeUploadPath>$([System.IO.Path]::Combine('$(PowerForgeStoreOutputRoot)', 'FakeApp.msixupload'))</PowerForgeUploadPath>
      <PowerForgeSymbolsPath>$([System.IO.Path]::Combine('$(PowerForgeStoreOutputRoot)', 'FakeApp.appxsym'))</PowerForgeSymbolsPath>
    </PropertyGroup>
    <MakeDir Directories="$(PowerForgeStoreOutputRoot)" />
    <WriteLinesToFile File="$(PowerForgeBundlePath)" Lines="bundle" Overwrite="true" />
    <WriteLinesToFile File="$(PowerForgeUploadPath)" Lines="upload" Overwrite="true" />
    <WriteLinesToFile File="$(PowerForgeSymbolsPath)" Lines="symbols" Overwrite="true" />
  </Target>
</Project>
""".Replace("OUTPUT_ROOT_TOKEN", outputRootExpression, StringComparison.Ordinal), new UTF8Encoding(false));
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
