using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class PrivateModuleWorkflowServiceTests
{
    [Fact]
    public void Execute_InstallRepositoryMode_UsesDependencyExecutorWithExpectedRequest()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        PrivateModuleDependencyExecutionRequest? capturedRequest = null;
        string? capturedTarget = null;
        string? capturedAction = null;
        var expected = new[]
        {
            new ModuleDependencyInstallResult("ModuleA", null, "1.0.0", null, ModuleDependencyInstallStatus.Installed, "PSResourceGet", null)
        };

        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            request =>
            {
                capturedRequest = request;
                return expected;
            });

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = new[] { "ModuleA", "ModuleA", "ModuleB" },
                RequiredVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModuleA"] = "1.2.0"
                },
                MinimumVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModuleB"] = "2.0.0"
                },
                MaximumVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModuleB"] = "2.5.0"
                },
                InstallScope = "CurrentUser",
                InstallScopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModuleA"] = "AllUsers"
                },
                UseAzureArtifacts = false,
                RepositoryName = "Company",
                Prerelease = true,
                Force = true
            },
            (target, action) =>
            {
                capturedTarget = target;
                capturedAction = action;
                return true;
            });

        Assert.True(result.OperationPerformed);
        Assert.Equal("Company", result.RepositoryName);
        Assert.Same(expected, result.DependencyResults);
        Assert.NotNull(capturedRequest);
        Assert.Equal(PrivateModuleWorkflowOperation.Install, capturedRequest!.Operation);
        Assert.Equal("Company", capturedRequest.RepositoryName);
        Assert.True(capturedRequest.Prerelease);
        Assert.True(capturedRequest.Force);
        Assert.False(capturedRequest.PreferPowerShellGet);
        Assert.Null(capturedRequest.Credential);
        Assert.Collection(
            capturedRequest.Modules,
            first =>
            {
                Assert.Equal("ModuleA", first.Name);
                Assert.Equal("1.2.0", first.RequiredVersion);
                Assert.Equal("AllUsers", first.InstallScope);
            },
            second =>
            {
                Assert.Equal("ModuleB", second.Name);
                Assert.Null(second.RequiredVersion);
                Assert.Equal("2.0.0", second.MinimumVersion);
                Assert.Equal("2.5.0", second.MaximumVersion);
                Assert.Equal("CurrentUser", second.InstallScope);
            });
        Assert.Equal("2 module(s) from repository 'Company'", capturedTarget);
        Assert.Equal("Install or reinstall private modules", capturedAction);
    }

    [Fact]
    public void Execute_AutoTransportWithRegisteredRepositoryName_UsesCompatibilityExecutor()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        PrivateModuleDependencyExecutionRequest? capturedRequest = null;
        var expected = new[]
        {
            new ModuleDependencyInstallResult("ModuleA", null, "1.0.0", null, ModuleDependencyInstallStatus.Installed, "PSResourceGet", null)
        };
        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            request =>
            {
                capturedRequest = request;
                return expected;
            });

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = new[] { "ModuleA" },
                UseAzureArtifacts = false,
                RepositoryName = "Company",
                DeliveryTransport = ModuleStateDeliveryTransport.Auto
            },
            (_, _) => true);

        Assert.True(result.OperationPerformed);
        Assert.Same(expected, result.DependencyResults);
        Assert.NotNull(capturedRequest);
        Assert.Equal("Company", capturedRequest!.RepositoryName);
        Assert.Equal("PSResourceGet", result.DependencyResults[0].Installer);
        Assert.Equal(ModuleStateDeliveryTransport.Auto, result.RequestedTransport);
        Assert.Equal(ModuleStateDeliveryTransport.PrivateModule, result.EffectiveTransport);
        Assert.Contains("registered repository name", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(host.VerboseMessages, message => message.Contains("using PrivateModule", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_AutoTransportWithRepositorySource_UsesManagedExecutor()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("1.0.0"));
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        var compatibilityExecutorCalled = false;
        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            _ =>
            {
                compatibilityExecutorCalled = true;
                return Array.Empty<ModuleDependencyInstallResult>();
            });

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = new[] { "Company.Tools" },
                RequiredVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Company.Tools"] = "1.0.0"
                },
                UseAzureArtifacts = false,
                RepositoryName = "Local",
                DeliveryTransport = ModuleStateDeliveryTransport.Auto,
                ManagedRepositorySource = feed.Path,
                ManagedModuleRoot = moduleRoot.Path
            },
            (_, _) => true);

        Assert.True(result.OperationPerformed);
        Assert.False(compatibilityExecutorCalled);
        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal("ManagedModule", dependency.Installer);
        Assert.Equal("1.0.0", dependency.ResolvedVersion);
        Assert.Equal(ModuleStateDeliveryTransport.Auto, result.RequestedTransport);
        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, result.EffectiveTransport);
        Assert.Contains("repository source", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(host.VerboseMessages, message => message.Contains("using ManagedModule", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void Execute_AutoTransportWithSupportedPrivateProvider_ReportsManagedSupportReason()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            _ => throw new InvalidOperationException("Compatibility executor should not run."));

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = new[] { "Company.Tools" },
                UseAzureArtifacts = true,
                Provider = PrivateGalleryProvider.NuGet,
                RepositoryName = "Company",
                RepositoryUri = "https://nuget.example.test/v3/index.json",
                DeliveryTransport = ModuleStateDeliveryTransport.Auto
            },
            (_, _) => false);

        Assert.False(result.OperationPerformed);
        Assert.Equal(ModuleStateDeliveryTransport.Auto, result.RequestedTransport);
        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, result.EffectiveTransport);
        Assert.Contains("Generic NuGet private feed", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supported", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedRequestMapping_PreservesExclusiveBoundsAsVersionPolicy()
    {
        var method = typeof(PrivateModuleWorkflowService).GetMethod(
            "ResolveManagedVersionPolicy",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var workflow = new PrivateModuleWorkflowRequest();
        var module = new ModuleDependency(
            "Company.Tools",
            minimumVersion: "1.0.0",
            maximumVersion: "2.0.0",
            maximumVersionInclusive: false);

        var policy = Assert.IsType<string>(method!.Invoke(null, new object[] { workflow, module }));

        Assert.Equal(">=1.0.0 <2.0.0", policy);
    }

    [Fact]
    public void ManagedRequestMapping_UsesModuleScopeBeforeWorkflowScope()
    {
        var method = typeof(PrivateModuleWorkflowService).GetMethod(
            "ResolveManagedScope",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null,
            new[] { typeof(PrivateModuleWorkflowRequest), typeof(ModuleDependency) },
            null);
        Assert.NotNull(method);
        var workflow = new PrivateModuleWorkflowRequest
        {
            ManagedScope = ManagedModuleInstallScope.CurrentUser
        };
        var module = new ModuleDependency("Company.Tools", installScope: "AllUsers");

        var scope = Assert.IsType<ManagedModuleInstallScope>(method!.Invoke(null, new object[] { workflow, module }));

        Assert.Equal(ManagedModuleInstallScope.AllUsers, scope);
    }

    [Fact]
    public void Execute_ExplicitManagedTransportWithPartialPrivateProvider_ReportsProviderLimitation()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            _ => throw new InvalidOperationException("Compatibility executor should not run."));

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = new[] { "Company.Tools" },
                UseAzureArtifacts = true,
                Provider = PrivateGalleryProvider.JFrog,
                RepositoryName = "Company",
                JFrogBaseUri = "https://company.jfrog.io/artifactory",
                JFrogRepository = "powershell-virtual",
                DeliveryTransport = ModuleStateDeliveryTransport.ManagedModule
            },
            (_, _) => false);

        Assert.False(result.OperationPerformed);
        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, result.RequestedTransport);
        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, result.EffectiveTransport);
        Assert.Contains("JFrog", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Partial", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OIDC", result.DeliveryTransportReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_UpdateRepositoryMode_SkipsDependencyExecutionWhenShouldProcessDeclines()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        var executorCalled = false;

        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            _ =>
            {
                executorCalled = true;
                return Array.Empty<ModuleDependencyInstallResult>();
            });

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Update,
                ModuleNames = new[] { "ModuleA" },
                UseAzureArtifacts = false,
                RepositoryName = "Company"
            },
            (_, _) => false);

        Assert.False(result.OperationPerformed);
        Assert.Equal("Company", result.RepositoryName);
        Assert.Empty(result.DependencyResults);
        Assert.False(executorCalled);
    }

    [Fact]
    public void Execute_UpdateRepositoryMode_LeavesInstallScopeUnsetWhenNotRequested()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        PrivateModuleDependencyExecutionRequest? capturedRequest = null;
        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            request =>
            {
                capturedRequest = request;
                return Array.Empty<ModuleDependencyInstallResult>();
            });

        service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Update,
                ModuleNames = new[] { "ModuleA" },
                UseAzureArtifacts = false,
                RepositoryName = "Company"
            },
            (_, _) => true);

        var module = Assert.Single(capturedRequest!.Modules);
        Assert.Null(module.InstallScope);
    }

    [Fact]
    public void Execute_RepositoryMode_DoesNotResolveOptionalCredentialsWhenShouldProcessDeclines()
    {
        var host = new FakePrivateGalleryHost();
        var galleryService = new PrivateGalleryService(host);
        var executorCalled = false;

        var service = new PrivateModuleWorkflowService(
            host,
            galleryService,
            new NullLogger(),
            _ =>
            {
                executorCalled = true;
                return Array.Empty<ModuleDependencyInstallResult>();
            });

        var result = service.Execute(
            new PrivateModuleWorkflowRequest
            {
                Operation = PrivateModuleWorkflowOperation.Install,
                ModuleNames = new[] { "ModuleA" },
                UseAzureArtifacts = false,
                RepositoryName = "Company",
                CredentialSecretFilePath = "missing-secret.txt"
            },
            (_, _) => false);

        Assert.False(result.OperationPerformed);
        Assert.Equal("Company", result.RepositoryName);
        Assert.Empty(result.DependencyResults);
        Assert.False(executorCalled);
    }

    private sealed class FakePrivateGalleryHost : IPrivateGalleryHost
    {
        internal List<string> VerboseMessages { get; } = new();

        public bool ShouldProcess(string target, string action) => true;

        public bool IsWhatIfRequested => false;

        public RepositoryCredential? PromptForCredential(string caption, string message) => null;

        public void WriteVerbose(string message)
        {
            VerboseMessages.Add(message);
        }

        public void WriteWarning(string message)
        {
        }
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };
}
