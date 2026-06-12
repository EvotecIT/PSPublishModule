using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed class RepositoryPublisherTests
{
    [Fact]
    public void Publish_exchanges_jfrog_oidc_token_before_psresourceget_publish()
    {
        var tokenEnvName = "POWERFORGE_TEST_JFROG_OIDC_" + Guid.NewGuid().ToString("N");
        var toolDir = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Directory.CreateDirectory(toolDir);
            File.WriteAllText(Path.Combine(toolDir, Path.DirectorySeparatorChar == '\\' ? "jf.exe" : "jf"), string.Empty);
            Environment.SetEnvironmentVariable("PATH", toolDir + Path.PathSeparator + originalPath);
            Environment.SetEnvironmentVariable(tokenEnvName, "ci-jwt");

            var powerShellRunner = new StubPowerShellRunner(_ => new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", string.Empty, "pwsh.exe"));
            var processRunner = new StubProcessRunner(_ => new ProcessRunResult(
                0,
                """{"access_token":"jfrog-access-token","username":"oidc-user@example.com"}""",
                string.Empty,
                "jf.exe",
                TimeSpan.FromMilliseconds(10),
                timedOut: false));
            var publisher = new RepositoryPublisher(new NullLogger(), powerShellRunner, processRunner);

            publisher.Publish(new RepositoryPublishRequest
            {
                Path = @"C:\staging\module",
                IsNupkg = false,
                RepositoryName = "JFrogPS",
                ApiKey = null,
                Tool = PublishTool.PSResourceGet,
                Repository = new PublishRepositoryConfiguration
                {
                    Name = "JFrogPS",
                    EnsureRegistered = false,
                    CredentialProvider = new RepositoryCredentialProviderConfiguration
                    {
                        Kind = RepositoryCredentialProviderKind.JFrogOidc,
                        JFrogPlatformUri = "https://company.jfrog.io/",
                        JFrogOidcProvider = "azure-oidc",
                        JFrogOidcProviderType = JFrogOidcProviderType.Azure,
                        JFrogOidcTokenIdEnvironmentVariable = tokenEnvName
                    }
                }
            });

            var processRequest = Assert.Single(processRunner.Requests);
            Assert.Equal(new[] { "eot", "azure-oidc", "--url=https://company.jfrog.io/", "--oidc-provider-type=Azure" }, processRequest.Arguments);
            Assert.Equal("ci-jwt", processRequest.EnvironmentVariables?["JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID"]);

            var powerShellRequest = Assert.Single(powerShellRunner.Requests);
            Assert.Equal("oidc-user@example.com", powerShellRequest.Arguments[7]);
            Assert.Equal("jfrog-access-token", powerShellRequest.Arguments[8]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnvName, null);
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(toolDir))
                Directory.Delete(toolDir, recursive: true);
        }
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public List<PowerShellRunRequest> Requests { get; } = new();

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            Requests.Add(request);
            return _run(request);
        }
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _run;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> run)
        {
            _run = run;
        }

        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_run(request));
        }
    }
}
