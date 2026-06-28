using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerMsiBuildTests
{
    [Fact]
    public void MsiBuildResult_ToString_UsesInstallerAndVersion()
    {
        var result = new DotNetPublishMsiBuildResult
        {
            InstallerId = "TierBridge.MSI",
            Version = "4.0.9498",
            VersionPropertyName = "ProductVersion",
            OutputFiles = new[] { @"C:\Build\TierBridge.msi" }
        };

        Assert.Equal(@"TierBridge.MSI 4.0.9498 -> C:\Build\TierBridge.msi", result.ToString());
    }

    [Fact]
    public void ResolveInstallerOutputName_AppliesTokensAndStripsMsiExtension()
    {
        var plan = new DotNetPublishPlan { Configuration = "Release" };
        var installer = new DotNetPublishInstallerPlan
        {
            Id = "syncse",
            OutputName = "{target}-{rid}-{version}.msi"
        };
        var step = new DotNetPublishStep
        {
            TargetName = "GraphEssentialsX.Sync.Service",
            Runtime = "win-x64",
            Framework = "net8.0",
            Style = DotNetPublishStyle.PortableCompat
        };

        var outputName = DotNetPublishPipelineRunner.ResolveInstallerOutputName(
            plan,
            installer,
            step,
            "1.0.9638");

        Assert.Equal("GraphEssentialsX.Sync.Service-win-x64-1.0.9638", outputName);
    }

    [Fact]
    public void PrepareGeneratedInstallerBuildWorkspace_CopiesProjectToShortTempWorkspace()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "LocalPackages"));
            Directory.CreateDirectory(Path.Combine(root, "GlobalPackages"));
            Directory.CreateDirectory(Path.Combine(root, "LocalFallback"));
            Directory.CreateDirectory(Path.Combine(root, "Artifacts", "DotNetPublish", "NestedPackages"));
            File.WriteAllText(
                Path.Combine(root, "NuGet.config"),
                "<configuration><config>" +
                "<add key=\"globalPackagesFolder\" value=\"GlobalPackages\" />" +
                "</config><packageSources>" +
                "<add key=\"local\" value=\"LocalPackages\" />" +
                "<add key=\"file\" value=\"file:///C:/packages\" />" +
                "<add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\" />" +
                "</packageSources><fallbackPackageFolders>" +
                "<add key=\"fallback\" value=\"LocalFallback\" />" +
                "</fallbackPackageFolders></configuration>");
            var importedBuildProps = Path.Combine(root, "Build", "Props", "Generated.props");
            Directory.CreateDirectory(Path.GetDirectoryName(importedBuildProps)!);
            File.WriteAllText(importedBuildProps, "<Project />");
            var rootBuildTargets = Path.Combine(root, "Directory.Build.targets");
            File.WriteAllText(rootBuildTargets, "<Project />");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><Import Project=\"$(MSBuildThisFileDirectory)Build\\**\\*.props\" /></Project>");
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), "<Project />");
            var sourceDir = Path.Combine(
                root,
                "Artifacts",
                "DotNetPublish",
                "Msi",
                "DesktopManager.App.MSI",
                "DesktopManager.App",
                "win-x64",
                "net10.0-windows10.0.19041.0",
                "PortableCompat",
                "prepare",
                "generated");
            var nestedConfigPath = Path.Combine(root, "Artifacts", "DotNetPublish", "Directory.Build.targets");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedConfigPath)!);
            File.WriteAllText(
                Path.Combine(root, "Artifacts", "DotNetPublish", "NuGet.config"),
                "<configuration><packageSources>" +
                "<add key=\"nested\" value=\"NestedPackages\" />" +
                "</packageSources></configuration>");
            File.WriteAllText(
                nestedConfigPath,
                "<Project><PropertyGroup>" +
                "<RepoToolPath>$(MSBuildThisFileDirectory)Tools</RepoToolPath>" +
                "</PropertyGroup>" +
                "<Import Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)..'))\" " +
                "Condition=\"Exists('$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)..'))')\" />" +
                "</Project>");
            Directory.CreateDirectory(Path.Combine(sourceDir, "Fragments"));
            var sourceProjectPath = Path.Combine(sourceDir, "DesktopManager_App_MSI.wixproj");
            var stagingDir = Path.Combine(root, "Payload", "DesktopManager.App", "win-x64", "net10.0-windows10.0.19041.0", "PortableCompat");
            Directory.CreateDirectory(stagingDir);
            var payloadFile = Path.Combine(stagingDir, "DesktopManager.App.exe");
            File.WriteAllText(payloadFile, "payload");
            var firstExternalAsset = Path.Combine(root, "External", "One", "duplicate.txt");
            var secondExternalAsset = Path.Combine(root, "External", "Two", "duplicate.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(firstExternalAsset)!);
            Directory.CreateDirectory(Path.GetDirectoryName(secondExternalAsset)!);
            File.WriteAllText(firstExternalAsset, "one");
            File.WriteAllText(secondExternalAsset, "two");
            var includeCaseDistinctAssets = IsCurrentFileSystemCaseSensitive(root);
            var firstCaseDistinctAsset = Path.Combine(root, "External", "Case", "asset.txt");
            var secondCaseDistinctAsset = Path.Combine(root, "External", "Case", "ASSET.txt");
            var caseDistinctFileEntries = string.Empty;
            if (includeCaseDistinctAssets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(firstCaseDistinctAsset)!);
                File.WriteAllText(firstCaseDistinctAsset, "lower");
                File.WriteAllText(secondCaseDistinctAsset, "upper");
                caseDistinctFileEntries =
                    $"<File Id=\"CaseLower\" Source=\"{firstCaseDistinctAsset}\" />" +
                    $"<File Id=\"CaseUpper\" Source=\"{secondCaseDistinctAsset}\" />";
            }
            var licensePath = Path.Combine(root, "Build", "Installer", "DesktopManager-License.rtf");
            Directory.CreateDirectory(Path.GetDirectoryName(licensePath)!);
            File.WriteAllText(licensePath, "{\\rtf1 DesktopManager}");
            var harvestPath = Path.Combine(root, "Artifacts", "DotNetPublish", "Msi", "DesktopManager.App.MSI", "HarvestedPayload.wxs");
            Directory.CreateDirectory(Path.GetDirectoryName(harvestPath)!);
            File.WriteAllText(
                harvestPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">" +
                "<Fragment><DirectoryRef Id=\"INSTALLFOLDER\"><Component Id=\"Payload\" Guid=\"*\">" +
                $"<File Id=\"DesktopManagerAppExe\" Source=\"{payloadFile}\" KeyPath=\"yes\" />" +
                "</Component></DirectoryRef></Fragment></Wix>");
            File.WriteAllText(
                sourceProjectPath,
                "<Project Sdk=\"WixToolset.Sdk/4.0.6\">" +
                "<PropertyGroup>" +
                $"<DefineConstants>PayloadDir={stagingDir};PowerForgeMsiPayloadDir={stagingDir};Other=1</DefineConstants>" +
                "</PropertyGroup>" +
                "<ItemGroup>" +
                "<Compile Include=\"Product.wxs\" />" +
                $"<Compile Include=\"{harvestPath}\" />" +
                "</ItemGroup></Project>");
            File.WriteAllText(
                Path.Combine(sourceDir, "Product.wxs"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">" +
                "<Package Name=\"DesktopManager\">" +
                $"<WixVariable Id=\"WixUILicenseRtf\" Value=\"{licensePath}\" />" +
                $"<File Id=\"LiteralPayloadExe\" Source=\"{payloadFile}\" />" +
                $"<File Id=\"ExternalOne\" Source=\"{firstExternalAsset}\" />" +
                $"<File Id=\"ExternalTwo\" Source=\"{secondExternalAsset}\" />" +
                caseDistinctFileEntries +
                "</Package></Wix>");
            File.WriteAllText(Path.Combine(sourceDir, "Fragments", "Payload.wxs"), "<Wix />");

            var workspace = DotNetPublishPipelineRunner.PrepareGeneratedInstallerBuildWorkspace(
                "DesktopManager.App.MSI",
                sourceDir,
                sourceProjectPath,
                new DotNetPublishMsiPrepareResult
                {
                    InstallerId = "DesktopManager.App.MSI",
                    Target = "DesktopManager.App",
                    Framework = "net10.0-windows10.0.19041.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    StagingDir = stagingDir,
                    HarvestPath = harvestPath
                },
                root);
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge", "WixBuild"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            try
            {
                Assert.StartsWith(tempRoot, workspace.WorkingDirectory, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.Equals(sourceDir, workspace.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
                Assert.True(File.Exists(workspace.ProjectPath));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "Product.wxs")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "Fragments", "Payload.wxs")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "NuGet.config")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "Directory.Build.props")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "Directory.Build.targets")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "Directory.Packages.props")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "Build", "Props", "Generated.props")));
                Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "PowerForgeInputs", "BuildConfig", "Directory.Build.targets")));
                var copiedNestedTargets = XDocument.Load(Path.Combine(workspace.WorkingDirectory, "Directory.Build.targets"));
                var copiedNestedImportElement = copiedNestedTargets
                    .Descendants()
                    .Single(element => string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase));
                var copiedNestedImport = copiedNestedImportElement
                    .Attribute("Project")!
                    .Value;
                Assert.DoesNotContain("GetPathOfFileAbove", copiedNestedImport, StringComparison.OrdinalIgnoreCase);
                var copiedOuterTargetPath = Path.GetFullPath(Path.Combine(workspace.WorkingDirectory, copiedNestedImport));
                Assert.True(File.Exists(copiedOuterTargetPath));
                Assert.Contains("PowerForgeInputs", copiedNestedImport, StringComparison.OrdinalIgnoreCase);
                var copiedNestedImportCondition = copiedNestedImportElement.Attribute("Condition")!.Value;
                Assert.DoesNotContain("GetPathOfFileAbove", copiedNestedImportCondition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(copiedNestedImport, copiedNestedImportCondition, StringComparison.OrdinalIgnoreCase);
                var repoToolPath = copiedNestedTargets
                    .Descendants()
                    .Single(element => string.Equals(element.Name.LocalName, "RepoToolPath", StringComparison.OrdinalIgnoreCase))
                    .Value;
                Assert.DoesNotContain("MSBuildThisFileDirectory", repoToolPath, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(root, "Artifacts", "DotNetPublish", "Tools")),
                    Path.GetFullPath(repoToolPath));
                var copiedNuGetConfig = XDocument.Load(Path.Combine(workspace.WorkingDirectory, "NuGet.config"));
                var localPackageSource = copiedNuGetConfig
                    .Descendants()
                    .Single(element =>
                        string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)element.Attribute("key"), "local", StringComparison.OrdinalIgnoreCase))
                    .Attribute("value")!
                    .Value;
                Assert.Equal(Path.GetFullPath(Path.Combine(root, "LocalPackages")), Path.GetFullPath(localPackageSource));
                Assert.DoesNotContain(workspace.WorkingDirectory, localPackageSource, StringComparison.OrdinalIgnoreCase);
                var filePackageSource = copiedNuGetConfig
                    .Descendants()
                    .Single(element =>
                        string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)element.Attribute("key"), "file", StringComparison.OrdinalIgnoreCase))
                    .Attribute("value")!
                    .Value;
                Assert.Equal("file:///C:/packages", filePackageSource);
                var nestedPackageSource = copiedNuGetConfig
                    .Descendants()
                    .Single(element =>
                        string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)element.Attribute("key"), "nested", StringComparison.OrdinalIgnoreCase))
                    .Attribute("value")!
                    .Value;
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(root, "Artifacts", "DotNetPublish", "NestedPackages")),
                    Path.GetFullPath(nestedPackageSource));
                var globalPackagesFolder = copiedNuGetConfig
                    .Descendants()
                    .Single(element =>
                        string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)element.Attribute("key"), "globalPackagesFolder", StringComparison.OrdinalIgnoreCase))
                    .Attribute("value")!
                    .Value;
                Assert.Equal(Path.GetFullPath(Path.Combine(root, "GlobalPackages")), Path.GetFullPath(globalPackagesFolder));
                var fallbackPackageFolder = copiedNuGetConfig
                    .Descendants()
                    .Single(element =>
                        string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)element.Attribute("key"), "fallback", StringComparison.OrdinalIgnoreCase))
                    .Attribute("value")!
                    .Value;
                Assert.Equal(Path.GetFullPath(Path.Combine(root, "LocalFallback")), Path.GetFullPath(fallbackPackageFolder));
                Assert.NotNull(workspace.PayloadDirectory);
                Assert.NotNull(workspace.HarvestPath);
                Assert.True(File.Exists(Path.Combine(workspace.PayloadDirectory!, "DesktopManager.App.exe")));
                Assert.True(File.Exists(workspace.HarvestPath!));

                var project = XDocument.Load(workspace.ProjectPath);
                var projectText = project.ToString();
                Assert.DoesNotContain(harvestPath, projectText, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(stagingDir, projectText, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(workspace.PayloadDirectory!, projectText, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("PowerForgeInputs", projectText, StringComparison.OrdinalIgnoreCase);

                XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";
                var harvest = XDocument.Load(workspace.HarvestPath!);
                var source = harvest
                    .Descendants(wix + "File")
                    .Single()
                    .Attribute("Source")!
                    .Value;
                Assert.DoesNotContain(stagingDir, source, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(workspace.PayloadDirectory!, source, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(source));

                var product = XDocument.Load(Path.Combine(workspace.WorkingDirectory, "Product.wxs"));
                var licenseValue = product
                    .Descendants(wix + "WixVariable")
                    .Single()
                    .Attribute("Value")!
                    .Value;
                Assert.DoesNotContain(licensePath, licenseValue, StringComparison.OrdinalIgnoreCase);
                var copiedLicensePath = Path.GetFullPath(Path.Combine(workspace.WorkingDirectory, licenseValue));
                Assert.StartsWith(Path.Combine(workspace.WorkingDirectory, "PowerForgeInputs", "Assets"), copiedLicensePath, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(copiedLicensePath));
                var literalSource = product
                    .Descendants(wix + "File")
                    .Single(element => string.Equals((string?)element.Attribute("Id"), "LiteralPayloadExe", StringComparison.OrdinalIgnoreCase))
                    .Attribute("Source")!
                    .Value;
                Assert.DoesNotContain(stagingDir, literalSource, StringComparison.OrdinalIgnoreCase);
                var copiedLiteralSource = Path.GetFullPath(Path.Combine(workspace.WorkingDirectory, literalSource));
                Assert.StartsWith(workspace.PayloadDirectory!, copiedLiteralSource, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(copiedLiteralSource));
                var externalSources = product
                    .Descendants(wix + "File")
                    .Where(element =>
                    {
                        var id = (string?)element.Attribute("Id");
                        return string.Equals(id, "ExternalOne", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(id, "ExternalTwo", StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(element => Path.GetFullPath(Path.Combine(
                        workspace.WorkingDirectory,
                        element.Attribute("Source")!.Value)))
                    .ToArray();
                Assert.Equal(2, externalSources.Length);
                Assert.All(externalSources, path =>
                {
                    Assert.Equal("duplicate.txt", Path.GetFileName(path));
                    Assert.True(File.Exists(path));
                    Assert.StartsWith(Path.Combine(workspace.WorkingDirectory, "PowerForgeInputs", "Assets"), path, StringComparison.OrdinalIgnoreCase);
                });
                Assert.NotEqual(
                    Path.GetDirectoryName(externalSources[0]),
                    Path.GetDirectoryName(externalSources[1]),
                    StringComparer.OrdinalIgnoreCase);
                if (includeCaseDistinctAssets)
                {
                    var caseDistinctSources = product
                        .Descendants(wix + "File")
                        .Where(element =>
                        {
                            var id = (string?)element.Attribute("Id");
                            return string.Equals(id, "CaseLower", StringComparison.Ordinal) ||
                                string.Equals(id, "CaseUpper", StringComparison.Ordinal);
                        })
                        .Select(element => Path.GetFullPath(Path.Combine(
                            workspace.WorkingDirectory,
                            element.Attribute("Source")!.Value)))
                        .ToArray();
                    Assert.Equal(2, caseDistinctSources.Length);
                    Assert.NotEqual(caseDistinctSources[0], caseDistinctSources[1], StringComparer.Ordinal);
                    Assert.Contains(caseDistinctSources, path => string.Equals(File.ReadAllText(path), "lower", StringComparison.Ordinal));
                    Assert.Contains(caseDistinctSources, path => string.Equals(File.ReadAllText(path), "upper", StringComparison.Ordinal));
                }
            }
            finally
            {
                var workingDirectory = workspace.WorkingDirectory;
                workspace.Dispose();
                Assert.False(Directory.Exists(workingDirectory));
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPublishMsBuildProperties_AppliesResolvedMsiVersion_WhenInstallerOptsIn()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, app);
            spec.Targets[0].Publish.Style = DotNetPublishStyle.PortableCompat;
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    Authoring = CreateSimpleAuthoring("ProductFiles"),
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        Major = 26,
                        Minor = 6,
                        FloorDateUtc = "2026-06-01",
                        Monotonic = false,
                        ApplyToPublish = true
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var target = Assert.Single(plan.Targets);
            var expectedVersion = $"26.6.{DaysSince20000101(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))}";

            var properties = DotNetPublishPipelineRunner.BuildPublishMsBuildProperties(
                plan,
                target,
                "net10.0",
                "win-x64",
                DotNetPublishStyle.PortableCompat);

            Assert.Equal(expectedVersion, properties["Version"]);
            Assert.Equal(expectedVersion, properties["PackageVersion"]);
            Assert.Equal($"{expectedVersion}.0", properties["FileVersion"]);
            Assert.Equal($"{expectedVersion}.0", properties["AssemblyVersion"]);
            Assert.Equal(expectedVersion, properties["InformationalVersion"]);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPublishArguments_OmitsNoBuildWhenApplyToPublishRequiresPayloadStamping()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, app);
            spec.DotNet.NoBuildInPublish = true;
            spec.Targets[0].Publish.Style = DotNetPublishStyle.PortableCompat;
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    Authoring = CreateSimpleAuthoring("ProductFiles"),
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        Major = 26,
                        Minor = 6,
                        FloorDateUtc = "2026-06-01",
                        Monotonic = false,
                        ApplyToPublish = true
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var target = Assert.Single(plan.Targets);
            var args = DotNetPublishPipelineRunner.BuildPublishArguments(
                plan,
                target,
                "net10.0",
                "win-x64",
                DotNetPublishStyle.PortableCompat,
                Path.Combine(root, "publish"));

            Assert.DoesNotContain("--no-build", args);
            Assert.Contains(args, arg => arg.Contains("Version=26.6.", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ApplyToPublishMsiVersions_AdvancesMonotonicStateAcrossCombinations()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, app);
            spec.Targets[0].Publish.Frameworks = new[] { "net10.0", "net8.0" };
            spec.Targets[0].Publish.Style = DotNetPublishStyle.PortableCompat;
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    Authoring = CreateSimpleAuthoring("ProductFiles"),
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        Major = 26,
                        Minor = 6,
                        FloorDateUtc = "2026-06-01",
                        Monotonic = true,
                        StatePath = "Artifacts/version.state.json",
                        ApplyToPublish = true
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var expectedPatch = DaysSince20000101(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var versions = plan.MsiVersions.Values.OrderBy(value => value.Patch).ToArray();

            Assert.Equal(2, versions.Length);
            Assert.Equal(expectedPatch, versions[0].Patch);
            Assert.Equal(expectedPatch + 1, versions[1].Patch);
            Assert.Equal(versions[0].StatePath, versions[1].StatePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPublishMsBuildProperties_ThrowsWhenApplyToPublishInstallersResolveDifferentVersions()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, app);
            spec.Targets[0].Publish.Style = DotNetPublishStyle.PortableCompat;
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app-a.msi",
                    PrepareFromTarget = "app",
                    Authoring = CreateSimpleAuthoring("ProductFiles"),
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        Major = 26,
                        Minor = 6,
                        FloorDateUtc = "2026-06-01",
                        Monotonic = false,
                        ApplyToPublish = true
                    }
                },
                new DotNetPublishInstaller
                {
                    Id = "app-b.msi",
                    PrepareFromTarget = "app",
                    Authoring = CreateSimpleAuthoring("ProductFiles"),
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        Major = 27,
                        Minor = 6,
                        FloorDateUtc = "2026-06-01",
                        Monotonic = false,
                        ApplyToPublish = true
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var target = Assert.Single(plan.Targets);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.BuildPublishMsBuildProperties(
                    plan,
                    target,
                    "net10.0",
                    "win-x64",
                    DotNetPublishStyle.PortableCompat));

            Assert.Contains("resolved publish property 'Version'", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveInstallerOutputDirectory_UsesConfiguredTemplate()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "syncse",
                OutputPath = "Artifacts/Msi/{installer}/{rid}"
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "syncse",
                TargetName = "app",
                Runtime = "win-x64",
                Framework = "net8.0",
                Style = DotNetPublishStyle.PortableCompat
            };
            var prepare = new DotNetPublishMsiPrepareResult { ManifestPath = string.Empty };

            var outputPath = DotNetPublishPipelineRunner.ResolveInstallerOutputDirectory(
                plan,
                installer,
                "syncse",
                step,
                prepare,
                version: null,
                isGeneratedInstallerProject: true);

            Assert.Equal(Path.Combine(root, "Artifacts", "Msi", "syncse", "win-x64"), outputPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveInstallerOutputDirectory_AppliesVersionToken()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "syncse",
                OutputPath = "Artifacts/Msi/{installer}/{version}"
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "syncse",
                TargetName = "app",
                Runtime = "win-x64",
                Framework = "net8.0",
                Style = DotNetPublishStyle.PortableCompat
            };
            var prepare = new DotNetPublishMsiPrepareResult { ManifestPath = string.Empty };

            var outputPath = DotNetPublishPipelineRunner.ResolveInstallerOutputDirectory(
                plan,
                installer,
                "syncse",
                step,
                prepare,
                version: "1.0.9646",
                isGeneratedInstallerProject: true);

            Assert.Equal(Path.Combine(root, "Artifacts", "Msi", "syncse", "1.0.9646"), outputPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FindChangedMsiOutputs_DetectsCustomOutputPathOutsideBin()
    {
        var root = CreateTempRoot();
        try
        {
            var outputPath = Path.Combine(root, "Artifacts", "Msi", "syncse", "SyncSE.msi");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "msi");

            string[] filtered = InvokeFindChangedMsiOutputs(root, skipBinDirectoryFilter: false);
            string[] unfiltered = InvokeFindChangedMsiOutputs(root, skipBinDirectoryFilter: true);

            Assert.Empty(filtered);
            Assert.Contains(Path.GetFullPath(outputPath), unfiltered);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveGeneratedInstallerOutputDirectory_UsesTemplateFallback_WhenManifestPathMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release"
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };
            var prepare = new DotNetPublishMsiPrepareResult
            {
                InstallerId = "app.msi",
                Target = "app",
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                ManifestPath = string.Empty
            };

            var outputDir = InvokeResolveGeneratedInstallerOutputDirectory(plan, step, prepare);

            Assert.Equal(
                Path.Combine(root, "Artifacts", "DotNetPublish", "Msi", "app.msi", "app", "win-x64", "net8.0", "Portable", "output"),
                outputDir);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AddsMsiBuildStep_WhenInstallerProjectPathConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectPath = "Installer/Package.wixproj"
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var buildStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiBuild);
            Assert.Equal(Path.GetFullPath(installer), Path.GetFullPath(buildStep.InstallerProjectPath!));

            var kinds = plan.Steps.Select(s => s.Kind).ToArray();
            var publishIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Publish);
            var prepareIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.MsiPrepare);
            var buildIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.MsiBuild);
            var manifestIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Manifest);

            Assert.True(publishIndex >= 0);
            Assert.True(prepareIndex > publishIndex);
            Assert.True(buildIndex > prepareIndex);
            Assert.True(manifestIndex > buildIndex);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AddsMsiBuildStep_WhenInstallerProjectIdConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Projects = new[]
            {
                new DotNetPublishProject { Id = "installer.project", Path = "Installer/Package.wixproj" }
            };
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectId = "installer.project"
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var buildStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiBuild);
            Assert.Equal(Path.GetFullPath(installer), Path.GetFullPath(buildStep.InstallerProjectPath!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AddsMsiBuildStep_WhenInstallerAuthoringConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");

            var spec = CreateBaseSpec(root, app);
            var authoring = CreateSimpleAuthoring("ProductFiles");
            authoring.ExitLaunch = new PowerForgeInstallerExitLaunch
            {
                Text = "Open app",
                Target = "http://127.0.0.1:9000/"
            };
            authoring.LicenseAgreement = new PowerForgeInstallerLicenseAgreement
            {
                Path = "Installer/App/License.rtf"
            };
            authoring.Inputs.Add(new PowerForgeInstallerInput
            {
                Id = "LicenseKey",
                PropertyName = "LICENSE_KEY",
                Label = "License key",
                Kind = PowerForgeInstallerInputKind.LicenseKey,
                Required = true,
                RequiredMessage = "Enter a license key before continuing.",
                MinLength = 16,
                MaxLength = 128,
                ValidationPattern = "^[A-Za-z0-9-]+$",
                ValidationMessage = "Enter a valid license key."
            });
            authoring.Dialogs.Add(new PowerForgeInstallerDialog
            {
                Id = "ConfigDlg",
                Title = "Configuration",
                InputIds = { "LicenseKey" },
                Actions =
                {
                    new PowerForgeInstallerDialogAction
                    {
                        Id = "OpenConfig",
                        Text = "Open config",
                        Target = "http://127.0.0.1:9000/config"
                    }
                }
            });
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    Harvest = DotNetPublishMsiHarvestMode.Auto,
                    Authoring = authoring
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var installerPlan = Assert.Single(plan.Installers);
            Assert.NotNull(installerPlan.Authoring);
            Assert.Equal("ProductFiles", installerPlan.HarvestComponentGroupId);
            Assert.NotNull(installerPlan.Authoring!.ExitLaunch);
            Assert.Equal("Open app", installerPlan.Authoring.ExitLaunch!.Text);
            Assert.Equal("http://127.0.0.1:9000/", installerPlan.Authoring.ExitLaunch.Target);
            Assert.NotNull(installerPlan.Authoring.LicenseAgreement);
            Assert.Equal("Installer/App/License.rtf", installerPlan.Authoring.LicenseAgreement!.Path);
            var input = Assert.Single(installerPlan.Authoring!.Inputs);
            Assert.True(input.Required);
            Assert.Equal("Enter a license key before continuing.", input.RequiredMessage);
            Assert.Equal(16, input.MinLength);
            Assert.Equal(128, input.MaxLength);
            Assert.Equal("^[A-Za-z0-9-]+$", input.ValidationPattern);
            Assert.Equal("Enter a valid license key.", input.ValidationMessage);
            var dialog = Assert.Single(installerPlan.Authoring.Dialogs);
            var action = Assert.Single(dialog.Actions);
            Assert.Equal("OpenConfig", action.Id);
            Assert.Equal("http://127.0.0.1:9000/config", action.Target);

            var prepareStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiPrepare);
            Assert.Equal("ProductFiles", prepareStep.HarvestComponentGroupId);
            var buildStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiBuild);
            Assert.Null(buildStep.InstallerProjectPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ClonesInstallerAuthoringLaunchConditionsAndServiceCredentials()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");

            var spec = CreateBaseSpec(root, app);
            var authoring = CreateSimpleAuthoring("ProductFiles");
            authoring.Inputs.Add(new PowerForgeInstallerInput
            {
                Id = "ServiceAccount",
                PropertyName = "SERVICE_ACCOUNT",
                Label = "Service account",
                DefaultValue = "LocalSystem"
            });
            authoring.Inputs.Add(new PowerForgeInstallerInput
            {
                Id = "ServicePassword",
                PropertyName = "SERVICE_PASSWORD",
                Label = "Service password",
                Secure = true,
                Hidden = true
            });
            authoring.LaunchConditions.Add(new PowerForgeInstallerLaunchCondition
            {
                Condition = "SERVICE_ACCOUNT = \"\" OR SERVICE_ACCOUNT = \"LocalSystem\" OR SERVICE_PASSWORD <> \"\"",
                Message = "Password is required when specifying a service account."
            });
            authoring.Components.Add(new PowerForgeInstallerServiceComponent
            {
                Id = "ServiceComponent",
                FileId = "AppServiceExe",
                Source = "$(var.PayloadDir)\\App.Service.exe",
                ServiceName = "Evotec.App.Service",
                DisplayName = "Evotec App Service",
                AccountPropertyName = "SERVICE_ACCOUNT",
                PasswordPropertyName = "SERVICE_PASSWORD"
            });
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    Harvest = DotNetPublishMsiHarvestMode.Auto,
                    Authoring = authoring
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var installerPlan = Assert.Single(plan.Installers);
            Assert.NotNull(installerPlan.Authoring);
            var plannedAuthoring = installerPlan.Authoring!;
            var condition = Assert.Single(plannedAuthoring.LaunchConditions);
            Assert.Equal("SERVICE_ACCOUNT = \"\" OR SERVICE_ACCOUNT = \"LocalSystem\" OR SERVICE_PASSWORD <> \"\"", condition.Condition);
            Assert.Equal("Password is required when specifying a service account.", condition.Message);
            var service = Assert.IsType<PowerForgeInstallerServiceComponent>(
                Assert.Single(plannedAuthoring.Components));
            Assert.Equal("SERVICE_ACCOUNT", service.AccountPropertyName);
            Assert.Equal("SERVICE_PASSWORD", service.PasswordPropertyName);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_AddsMsiSignStep_WhenInstallerSignEnabled()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectPath = "Installer/Package.wixproj",
                    Sign = new DotNetPublishSignOptions
                    {
                        Enabled = true,
                        OnMissingTool = DotNetPublishPolicyMode.Warn,
                        OnSignFailure = DotNetPublishPolicyMode.Warn
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var buildStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiBuild);
            var signStep = Assert.Single(plan.Steps, s => s.Kind == DotNetPublishStepKind.MsiSign);
            Assert.Equal(Path.GetFullPath(installer), Path.GetFullPath(buildStep.InstallerProjectPath!));
            Assert.Equal("app.msi", signStep.InstallerId);

            var kinds = plan.Steps.Select(s => s.Kind).ToArray();
            var buildIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.MsiBuild);
            var signIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.MsiSign);
            var manifestIndex = Array.FindIndex(kinds, k => k == DotNetPublishStepKind.Manifest);

            Assert.True(buildIndex >= 0);
            Assert.True(signIndex > buildIndex);
            Assert.True(manifestIndex > signIndex);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenInstallerProjectIdIsUnknown()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateBaseSpec(root, app);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectId = "missing"
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("InstallerProjectId", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenClientLicenseEnabledWithoutPathOrClientId()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectPath = installer,
                    ClientLicense = new DotNetPublishMsiClientLicenseOptions
                    {
                        Enabled = true
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("ClientLicense", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_MsiBuildStepWithoutPrepare_FailsWithMsiBuildFailureDetails()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "app.msi",
                        PrepareFromTarget = "app",
                        InstallerProjectPath = Path.Combine(root, "Installer", "Package.wixproj")
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep
                    {
                        Key = "msi.build:app.msi:app:net10.0:win-x64:Portable",
                        Kind = DotNetPublishStepKind.MsiBuild,
                        Title = "MSI build",
                        InstallerId = "app.msi",
                        TargetName = "app",
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.Portable,
                        InstallerProjectPath = Path.Combine(root, "Installer", "Package.wixproj")
                    }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
            Assert.Equal(DotNetPublishStepKind.MsiBuild, result.Failure!.StepKind);
            Assert.Equal("app.msi", result.Failure.InstallerId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_MsiSignStepWithoutBuild_FailsWithMsiSignFailureDetails()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "app.msi",
                        PrepareFromTarget = "app",
                        Sign = new DotNetPublishSignOptions
                        {
                            Enabled = true,
                            OnMissingTool = DotNetPublishPolicyMode.Warn,
                            OnSignFailure = DotNetPublishPolicyMode.Warn
                        }
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep
                    {
                        Key = "msi.sign:app.msi:app:net10.0:win-x64:Portable",
                        Kind = DotNetPublishStepKind.MsiSign,
                        Title = "MSI sign",
                        InstallerId = "app.msi",
                        TargetName = "app",
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.Portable
                    }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
            Assert.Equal(DotNetPublishStepKind.MsiSign, result.Failure!.StepKind);
            Assert.Equal("app.msi", result.Failure.InstallerId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveMsiVersion_UsesDateFloorPolicy()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                Versioning = new DotNetPublishMsiVersionOptions
                {
                    Enabled = true,
                    Major = 2,
                    Minor = 3,
                    FloorDateUtc = "2026-01-01",
                    Monotonic = false
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var result = InvokeResolveMsiVersion(plan, installer, step);
            var expectedPatch = DaysSince20000101(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal($"2.3.{expectedPatch}", result.Version);
            Assert.Equal(expectedPatch, result.Patch);
            Assert.Equal("ProductVersion", result.PropertyName);
            Assert.Null(result.StatePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveMsiVersion_UsesMonotonicState_WhenEnabled()
    {
        var root = CreateTempRoot();
        try
        {
            var statePath = Path.Combine(root, "state", "version.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, "{ \"lastPatch\": 41000 }");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                Versioning = new DotNetPublishMsiVersionOptions
                {
                    Enabled = true,
                    Major = 1,
                    Minor = 0,
                    FloorDateUtc = "2024-01-01",
                    Monotonic = true,
                    StatePath = "state/version.json"
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var result = InvokeResolveMsiVersion(plan, installer, step);

            Assert.Equal("1.0.41001", result.Version);
            Assert.Equal(41001, result.Patch);
            Assert.Equal(Path.GetFullPath(statePath), Path.GetFullPath(result.StatePath!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveMsiVersion_UsesUtcShortYearMonthDayMinutePattern()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                Versioning = new DotNetPublishMsiVersionOptions
                {
                    Enabled = true,
                    Pattern = DotNetPublishMsiVersionPattern.UtcShortYearMonthDayMinute,
                    FloorDateUtc = "2099-06-02",
                    Monotonic = false
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var result = InvokeResolveMsiVersion(plan, installer, step);

            Assert.Equal("99.6.2880", result.Version);
            Assert.Equal(2880, result.Patch);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveMsiVersion_DoesNotBumpPatchWhenPreviousStateVersionIsOlder()
    {
        var root = CreateTempRoot();
        try
        {
            var statePath = Path.Combine(root, "state", "version.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, "{ \"lastPatch\": 9499, \"version\": \"1.0.9499\" }");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                Versioning = new DotNetPublishMsiVersionOptions
                {
                    Enabled = true,
                    Pattern = DotNetPublishMsiVersionPattern.UtcShortYearMonthDayMinute,
                    FloorDateUtc = "2099-06-02",
                    Monotonic = true,
                    StatePath = "state/version.json"
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var result = InvokeResolveMsiVersion(plan, installer, step);

            Assert.Equal("99.6.2880", result.Version);
            Assert.Equal(2880, result.Patch);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveMsiVersion_CapsPatchAtConfiguredLimit()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                AllowOutputOutsideProjectRoot = false,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                Versioning = new DotNetPublishMsiVersionOptions
                {
                    Enabled = true,
                    Major = 1,
                    Minor = 0,
                    FloorDateUtc = "2300-01-01",
                    Monotonic = false,
                    PatchCap = 65535
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var result = InvokeResolveMsiVersion(plan, installer, step);
            Assert.Equal("1.0.65535", result.Version);
            Assert.Equal(65535, result.Patch);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenMsiVersionFloorDateIsInvalid()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectPath = installer,
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        FloorDateUtc = "not-a-date"
                    }
                }
            };

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            Assert.Contains("FloorDateUtc", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_NormalizesInstallerClientLicense_WhenEnabled()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var installer = CreateProject(root, "Installer/Package.wixproj");

            var spec = CreateBaseSpec(root, app);
            spec.Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "app.msi",
                    PrepareFromTarget = "app",
                    InstallerProjectPath = installer,
                    ClientLicense = new DotNetPublishMsiClientLicenseOptions
                    {
                        Enabled = true,
                        ClientId = " Acme ",
                        PathTemplate = " Installer/Clients/{clientId}/{target}.txlic ",
                        PropertyName = " ClientLicensePath "
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var installerPlan = Assert.Single(plan.Installers);
            Assert.NotNull(installerPlan.ClientLicense);
            Assert.True(installerPlan.ClientLicense!.Enabled);
            Assert.Equal("Acme", installerPlan.ClientLicense.ClientId);
            Assert.Equal("Installer/Clients/{clientId}/{target}.txlic", installerPlan.ClientLicense.PathTemplate);
            Assert.Equal("ClientLicensePath", installerPlan.ClientLicense.PropertyName);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveInstallerClientLicense_UsesClientIdTemplate_WhenFileExists()
    {
        var root = CreateTempRoot();
        try
        {
            var licensePath = Path.Combine(root, "Installer", "Clients", "Acme", "app.txlic");
            Directory.CreateDirectory(Path.GetDirectoryName(licensePath)!);
            File.WriteAllText(licensePath, "license");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                ClientLicense = new DotNetPublishMsiClientLicenseOptions
                {
                    Enabled = true,
                    ClientId = "Acme",
                    PathTemplate = "Installer/Clients/{clientId}/{target}.txlic",
                    PropertyName = "ClientLicensePath",
                    OnMissingFile = DotNetPublishPolicyMode.Fail
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var result = InvokeResolveInstallerClientLicense(plan, installer, step);
            Assert.Equal(Path.GetFullPath(licensePath), Path.GetFullPath(result.Path!));
            Assert.Equal("ClientLicensePath", result.PropertyName);
            Assert.Equal("Acme", result.ClientId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveInstallerClientLicense_ThrowsWhenMissingAndPolicyFail()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release"
            };
            var installer = new DotNetPublishInstallerPlan
            {
                Id = "app.msi",
                ClientLicense = new DotNetPublishMsiClientLicenseOptions
                {
                    Enabled = true,
                    ClientId = "Acme",
                    PathTemplate = "Installer/Clients/{clientId}/{target}.txlic",
                    PropertyName = "ClientLicensePath",
                    OnMissingFile = DotNetPublishPolicyMode.Fail
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };

            var ex = Assert.Throws<TargetInvocationException>(
                () => InvokeResolveInstallerClientLicense(plan, installer, step));
            Assert.NotNull(ex.InnerException);
            Assert.Contains("client license file was not found", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveOrPrepareInstallerProjectPath_GeneratesWixProjectFromAuthoring()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = Path.Combine(root, "Artifacts", "payload");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "App.exe"), "payload");
            var harvestPath = Path.Combine(root, "Artifacts", "harvest.wxs");
            Directory.CreateDirectory(Path.GetDirectoryName(harvestPath)!);
            File.WriteAllText(
                harvestPath,
                "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\"><Fragment><ComponentGroup Id=\"ProductFiles\" /></Fragment></Wix>");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Debug",
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "app.msi",
                        PrepareFromTarget = "app",
                        Authoring = CreateSimpleAuthoring("ProductFiles")
                    }
                }
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };
            var prepare = new DotNetPublishMsiPrepareResult
            {
                InstallerId = "app.msi",
                Target = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                StagingDir = staging,
                ManifestPath = Path.Combine(root, "Artifacts", "prepare.manifest.json"),
                HarvestPath = harvestPath,
                HarvestDirectoryRefId = "INSTALLFOLDER",
                HarvestComponentGroupId = "ProductFiles"
            };

            var projectPath = InvokeResolveOrPrepareInstallerProjectPath(plan, plan.Installers[0], step, prepare, "2.3.4");

            Assert.True(File.Exists(projectPath));
            Assert.Equal(
                Path.Combine(root, "Artifacts", "prepare.manifest", "generated"),
                Path.GetDirectoryName(projectPath));
            var outputDir = InvokeResolveGeneratedInstallerOutputDirectory(plan, step, prepare);
            Assert.Equal(Path.Combine(root, "Artifacts", "prepare.manifest", "output"), outputDir);
            var sourcePath = Path.Combine(Path.GetDirectoryName(projectPath)!, "Product.wxs");
            Assert.True(File.Exists(sourcePath));
            Assert.Contains("2.3.4", File.ReadAllText(sourcePath), StringComparison.Ordinal);
            var projectXml = File.ReadAllText(projectPath);
            Assert.Contains("Product.wxs", projectXml, StringComparison.Ordinal);
            Assert.Contains(harvestPath, projectXml, StringComparison.Ordinal);
            Assert.Contains("PayloadDir=", projectXml, StringComparison.Ordinal);
            Assert.Contains(staging, projectXml, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveGeneratedInstallerOutputDirectory_UsesManifestFileNameForIsolation()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release"
            };
            var step = new DotNetPublishStep
            {
                InstallerId = "app.msi",
                TargetName = "app",
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable
            };
            var prepareA = new DotNetPublishMsiPrepareResult
            {
                InstallerId = "app.msi",
                Target = "app",
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                ManifestPath = Path.Combine(root, "Artifacts", "prepare-a.json")
            };
            var prepareB = new DotNetPublishMsiPrepareResult
            {
                InstallerId = "app.msi",
                Target = "app",
                Framework = "net8.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.Portable,
                ManifestPath = Path.Combine(root, "Artifacts", "prepare-b.json")
            };

            var outputA = InvokeResolveGeneratedInstallerOutputDirectory(plan, step, prepareA);
            var outputB = InvokeResolveGeneratedInstallerOutputDirectory(plan, step, prepareB);

            Assert.Equal(Path.Combine(root, "Artifacts", "prepare-a", "output"), outputA);
            Assert.Equal(Path.Combine(root, "Artifacts", "prepare-b", "output"), outputB);
            Assert.NotEqual(outputA, outputB, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static (string? Version, string? PropertyName, int? Patch, string? StatePath) InvokeResolveMsiVersion(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan installer,
        DotNetPublishStep step)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("ResolveMsiVersion", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var raw = method!.Invoke(runner, new object?[] { plan, installer, step, null });
        Assert.NotNull(raw);

        var t = raw!.GetType();
        var version = t.GetProperty("Version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw) as string;
        var propertyName = t.GetProperty("PropertyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw) as string;
        var patchObj = t.GetProperty("Patch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw);
        var statePath = t.GetProperty("StatePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw) as string;

        return (version, propertyName, patchObj as int?, statePath);
    }

    private static (string? Path, string? PropertyName, string? ClientId) InvokeResolveInstallerClientLicense(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan installer,
        DotNetPublishStep step)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("ResolveInstallerClientLicense", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var raw = method!.Invoke(runner, new object?[] { plan, installer, step });
        Assert.NotNull(raw);

        var t = raw!.GetType();
        var path = t.GetProperty("Path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw) as string;
        var propertyName = t.GetProperty("PropertyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw) as string;
        var clientId = t.GetProperty("ClientId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(raw) as string;

        return (path, propertyName, clientId);
    }

    private static string InvokeResolveOrPrepareInstallerProjectPath(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan installer,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare,
        string productVersion)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("ResolveOrPrepareInstallerProjectPath", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var raw = method!.Invoke(runner, new object?[] { plan, step, installer, prepare, productVersion });
        Assert.NotNull(raw);
        return Assert.IsType<string>(raw);
    }

    private static string InvokeResolveGeneratedInstallerOutputDirectory(
        DotNetPublishPlan plan,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare)
    {
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("ResolveGeneratedInstallerOutputDirectory", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var raw = method!.Invoke(null, new object?[] { plan, step.InstallerId!, step, prepare });
        Assert.NotNull(raw);
        return Assert.IsType<string>(raw);
    }

    private static int DaysSince20000101(DateTime utcDate)
    {
        var floor = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (int)(utcDate - floor).TotalDays;
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

    private static PowerForgeInstallerDefinition CreateSimpleAuthoring(string payloadComponentGroupId)
    {
        return new PowerForgeInstallerDefinition
        {
            Product =
            {
                Name = "App",
                Manufacturer = "Evotec",
                Version = "1.0.0",
                UpgradeCode = "{13f69244-93ee-4df9-baf6-ec7afc7ebd32}"
            },
            CompanyFolderName = "Evotec",
            InstallDirectoryName = "App",
            PayloadComponentGroupId = payloadComponentGroupId
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static bool IsCurrentFileSystemCaseSensitive(string root)
    {
        var directory = Path.Combine(root, "CaseSensitivityProbe");
        Directory.CreateDirectory(directory);
        var lowerPath = Path.Combine(directory, "caseprobe");
        var upperPath = Path.Combine(directory, "CASEPROBE");
        File.WriteAllText(lowerPath, string.Empty);
        return !File.Exists(upperPath);
    }

    private static string[] InvokeFindChangedMsiOutputs(string root, bool skipBinDirectoryFilter)
    {
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("FindChangedMsiOutputs", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? raw = method!.Invoke(
            null,
            new object?[]
            {
                root,
                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase),
                skipBinDirectoryFilter
            });
        return Assert.IsType<string[]>(raw);
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
