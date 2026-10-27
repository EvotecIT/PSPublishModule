using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PSResourceGetFailureDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Failed_public_reads_preserve_failure_and_collect_opt_in_child_context(bool save, bool enabled)
    {
        using var root = new TemporaryDirectory();
        var client = new PSResourceGetClient(new FailureFixtureRunner(root.Path, enabled), new NullLogger());
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            if (save)
                client.Save(new PSResourceSaveOptions("PSWriteColor", root.Path, "1.0.7", "PSGallery"));
            else
                client.Find(new PSResourceFindOptions(new[] { "PSSharedGoods" }, version: "0.0.313", repositories: new[] { "PSGallery" }));
        });
        Assert.Contains("fixture-network-failure", failure.Message);
        Assert.Contains("failed (exit 1)", failure.Message);
        const string marker = "PFPSRG-DIAGNOSTIC: ";
        var offset = failure.Message.IndexOf(marker, StringComparison.Ordinal);
        Assert.Equal(enabled, offset >= 0);
        if (!enabled) return;
        using var report = JsonDocument.Parse(failure.Message[(offset + marker.Length)..]);
        Assert.Equal("1.2.0", report.RootElement.GetProperty("ModuleVersion").GetString());
        Assert.True(Version.TryParse(report.RootElement.GetProperty("PowerShellVersion").GetString(), out _));
        Assert.Equal("https://www.powershellgallery.com/api/v2", report.RootElement.GetProperty("RepositoryUri").GetString());
        Assert.Equal("NotExposedByBackend", report.RootElement.GetProperty("OriginalResponse").GetProperty("BodyStatus").GetString());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("PostFailureControl").ValueKind);
        var native = Assert.Single(report.RootElement.GetProperty("NativeFailureResponses").EnumerateArray());
        Assert.Equal(404, native.GetProperty("Status").GetInt32());
        Assert.Equal("OriginalNativeHttpClientResponseStop", native.GetProperty("CollectionPhase").GetString());
        Assert.Equal("fixture-MISS", native.GetProperty("Headers").GetProperty("X-Cache").GetString());
        Assert.Equal("NotCollected", native.GetProperty("RequestQuery").GetString());
        Assert.DoesNotContain("do-not-copy", failure.Message);
    }

    private sealed class FailureFixtureRunner(string root, bool enabled) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            // Replace only the native repository/backend boundary. The embedded
            // script, isolated process and returned failure protocol are real.
            var fixture = """
                $env:POWERFORGE_PUBLIC_GALLERY_DIAGNOSTICS = '__ENABLED__'
                function Import-Module { param($Name, $ErrorAction) }
                function Get-Module { param($Name) [pscustomobject]@{ Version = [Version]'1.2.0'; Path = 'fixture/PSResourceGet.psd1' } }
                function Get-PSResourceRepository { param($Name, $ErrorAction) [pscustomobject]@{ Uri = 'https://www.powershellgallery.com/api/v2' } }
                function Send-FixtureFailure {
                    Add-Type @'
                using System.Diagnostics;
                using System.Net;
                using System.Net.Http;
                public static class PFNativeFailureFixture {
                    public static void Emit() {
                        using (var listener = new DiagnosticListener("HttpHandlerDiagnosticListener"))
                        using (var request = new HttpRequestMessage(HttpMethod.Get, "https://www.powershellgallery.com/api/v2/package/PSWriteColor/1.0.7?token=do-not-copy"))
                        using (var response = new HttpResponseMessage(HttpStatusCode.NotFound)) {
                            response.RequestMessage = request;
                            response.Headers.TryAddWithoutValidation("X-Cache", "fixture-MISS");
                            response.Headers.TryAddWithoutValidation("Set-Cookie", "do-not-copy");
                            response.Content = new StringContent("do-not-copy");
                            listener.Write("System.Net.Http.HttpRequestOut.Stop", new { Response = response });
                        }
                    }
                }
                '@
                    [PFNativeFailureFixture]::Emit()
                    throw 'fixture-network-failure'
                }
                function Save-PSResource { Send-FixtureFailure }
                function Find-PSResource { Send-FixtureFailure }
                """.Replace("__ENABLED__", enabled ? "1" : "0");
            var script = File.ReadAllText(request.ScriptPath!);
            var scriptPath = Path.Combine(root, "failed-read.ps1");
            File.WriteAllText(scriptPath, script.Replace("$ProgressPreference = 'SilentlyContinue'",
                "$ProgressPreference = 'SilentlyContinue'\n" + fixture));
            return new PowerShellRunner().Run(new PowerShellRunRequest(scriptPath, request.Arguments,
                TimeSpan.FromSeconds(30), workingDirectory: root));
        }
    }
}
