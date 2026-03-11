using System;
using System.Collections.Generic;
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

    private sealed class FakePrivateGalleryHost : IPrivateGalleryHost
    {
        public bool ShouldProcess(string target, string action) => true;

        public bool IsWhatIfRequested => false;

        public RepositoryCredential? PromptForCredential(string caption, string message) => null;

        public void WriteVerbose(string message)
        {
        }

        public void WriteWarning(string message)
        {
        }
    }
}
