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
            first => Assert.Equal("ModuleA", first.Name),
            second => Assert.Equal("ModuleB", second.Name));
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
