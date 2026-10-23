using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class RepositoryDiagnosticServiceTests
{
    [Fact]
    public void Inspect_preserves_unknown_invocation_inputs_and_reports_competing_saved_authentication()
    {
        var snapshot = new RepositoryDiagnosticReport
        {
            RepositoryFound = true,
            RegisteredUri = "https://user:URL_SECRET@example.test/feed?token=QUERY_SECRET",
            Runtime = new RepositoryDiagnosticRuntime { ModuleAvailable = true, ModuleVersion = "1.2.0", NuGetVersion = "6.9.1.3", SettingsInspection = "Complete" },
            AuthenticationInputs = new[] {
                new RepositoryDiagnosticInput { Kind = "NuGetApiKey", Present = true, Match = "ExactEndpoint", Location = "user NuGet.Config" },
                new RepositoryDiagnosticInput { Kind = "EnvironmentCredential", Present = true, Match = "SourceName", Name = "NuGetPackageSourceCredentials_Corporate" }
            }
        };
        var runner = new SnapshotRunner(snapshot);
        var report = new RepositoryDiagnosticService(runner).Inspect(new RepositoryDiagnosticRequest { RepositoryName = "Corporate" });

        Assert.Null(report.CredentialSupplied);
        Assert.Null(report.ApiKeySupplied);
        Assert.Equal("Not reported by backend", report.AuthenticationSelection);
        Assert.Contains(report.Findings, f => f.Code == "PFREPO001");
        Assert.Contains(report.Findings, f => f.Code == "PFREPO002");
        Assert.DoesNotContain("URL_SECRET", JsonSerializer.Serialize(report));
        Assert.DoesNotContain("QUERY_SECRET", JsonSerializer.Serialize(report));
        Assert.NotNull(runner.ScriptPath);
        Assert.False(File.Exists(runner.ScriptPath));
    }

    [Fact]
    public void Same_host_api_key_is_inventory_and_not_an_exact_endpoint_conflict()
    {
        var report = new RepositoryDiagnosticReport
        {
            Runtime = new RepositoryDiagnosticRuntime { ModuleAvailable = true, SettingsInspection = "Complete" },
            AuthenticationInputs = new[] { new RepositoryDiagnosticInput { Kind = "NuGetApiKey", Present = true, Match = "SameHost" } }
        };
        RepositoryDiagnosticService.Complete(report);
        Assert.DoesNotContain(report.Findings, f => f.Code == "PFREPO001");
    }

    [Fact]
    public void Explicit_api_key_keeps_saved_key_inventory_without_a_competing_key_warning()
    {
        var snapshot = new RepositoryDiagnosticReport
        {
            Runtime = new RepositoryDiagnosticRuntime { ModuleAvailable = true, SettingsInspection = "Complete" },
            AuthenticationInputs = new[] { new RepositoryDiagnosticInput { Kind = "NuGetApiKey", Present = true, Effective = true, Match = "ExactEndpoint" } }
        };
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot)));
        var report = RepositoryDiagnosticService.PublishReport(PublishTool.PSResourceGet, RepositoryDiagnosticService.Marker + payload,
            "Corporate", "module", "explicit-key", null);
        Assert.True(report.ApiKeySupplied);
        Assert.Single(report.AuthenticationInputs);
        Assert.DoesNotContain(report.Findings, f => f.Code == "PFREPO001");
    }

    [Fact]
    public void Publish_failure_retains_context_and_redacts_plain_encoded_and_url_secrets()
    {
        var credential = new RepositoryCredential { UserName = "publisher", Secret = "sensitive+/token", Source = "Environment variable: BUILD_TOKEN" };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Secret));
        var message = $"401 Unauthorized {credential.Secret} {Uri.EscapeDataString(credential.Secret)} Basic {basic} API_SECRET https://someone:URL_SECRET@example.test/feed?token=QUERY_SECRET";
        var error = RepositoryDiagnosticService.PublishFailure(PublishTool.PSResourceGet, 1, message, "", "Corporate", "module", "API_SECRET", credential);

        Assert.True(error.Report.CredentialSupplied);
        Assert.Equal("Environment variable: BUILD_TOKEN", error.Report.CredentialSource);
        Assert.True(error.Report.ApiKeySupplied);
        Assert.Equal("Publish", error.Report.Operation);
        Assert.Null(error.InnerException);
        foreach (var secret in new[] { credential.Secret, Uri.EscapeDataString(credential.Secret), basic, "API_SECRET", "URL_SECRET", "QUERY_SECRET" })
        {
            Assert.DoesNotContain(secret, error.ToString());
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(error.Report));
        }
        Assert.Contains("authentication was rejected", error.Message);
    }

    [Theory]
    [InlineData(PublishTool.PSResourceGet)]
    [InlineData(PublishTool.PowerShellGet)]
    public void Native_publish_returns_report_without_changing_credential_handoff(PublishTool tool)
    {
        var runner = new SnapshotRunner(new RepositoryDiagnosticReport
        {
            Runtime = new RepositoryDiagnosticRuntime { ModuleAvailable = true, ModuleVersion = "1.2.0", PowerShellVersion = "7.6.6", SettingsInspection = "Complete" }
        });
        var result = new RepositoryPublisher(new NullLogger(), runner).Publish(new RepositoryPublishRequest
        {
            Path = "module", RepositoryName = "Corporate", Tool = tool,
            Repository = new PublishRepositoryConfiguration { EnsureRegistered = false, Credential = new RepositoryCredential { UserName = "publisher", Secret = new string('x', 6400) } }
        });
        Assert.NotNull(result.DiagnosticReport);
        Assert.True(result.DiagnosticReport.CredentialSupplied);
        Assert.Equal("7.6.6", result.DiagnosticReport.Runtime.PowerShellVersion);
        Assert.Equal(new string('x', 6400), runner.Arguments!.Last());
    }

    [Fact]
    public void Unreadable_settings_and_unavailable_module_are_reported_as_unknown()
    {
        var report = new RepositoryDiagnosticService(new SnapshotRunner(new RepositoryDiagnosticReport
        {
            Runtime = new RepositoryDiagnosticRuntime { SettingsInspection = "Unavailable" },
            ConfigurationFiles = new[] { new RepositoryDiagnosticFile { Path = "NuGet.Config", Status = "Unreadable" } }
        })).Inspect(new RepositoryDiagnosticRequest());
        Assert.Null(report.RepositoryFound);
        Assert.Equal("Unreadable", Assert.Single(report.ConfigurationFiles).Status);
        Assert.Contains(report.Findings, f => f.Code == "PFREPO003");
        Assert.Contains(report.Findings, f => f.Code == "PFREPO004");
    }

    private sealed class SnapshotRunner(RepositoryDiagnosticReport snapshot) : IPowerShellRunner
    {
        internal string? ScriptPath { get; private set; }
        internal IReadOnlyList<string>? Arguments { get; private set; }
        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            ScriptPath = request.ScriptPath;
            Arguments = request.Arguments;
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot)));
            return new PowerShellRunResult(0, RepositoryDiagnosticService.Marker + payload, "", "pwsh");
        }
    }
}
