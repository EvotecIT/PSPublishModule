using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            VersionPropertyName = "ProductVersion"
        };

        Assert.Equal("TierBridge.MSI 4.0.9498", result.ToString());
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
            var input = Assert.Single(installerPlan.Authoring!.Inputs);
            Assert.True(input.Required);
            Assert.Equal("Enter a license key before continuing.", input.RequiredMessage);
            Assert.Equal(16, input.MinLength);
            Assert.Equal(128, input.MaxLength);
            Assert.Equal("^[A-Za-z0-9-]+$", input.ValidationPattern);
            Assert.Equal("Enter a valid license key.", input.ValidationMessage);

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
                Path.Combine(root, "Artifacts", "generated"),
                Path.GetDirectoryName(projectPath));
            var outputDir = InvokeResolveGeneratedInstallerOutputDirectory(plan, step, prepare);
            Assert.Equal(Path.Combine(root, "Artifacts", "output"), outputDir);
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

    private static (string? Version, string? PropertyName, int? Patch, string? StatePath) InvokeResolveMsiVersion(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan installer,
        DotNetPublishStep step)
    {
        var runner = new DotNetPublishPipelineRunner(new NullLogger());
        var method = typeof(DotNetPublishPipelineRunner).GetMethod("ResolveMsiVersion", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var raw = method!.Invoke(runner, new object?[] { plan, installer, step });
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
