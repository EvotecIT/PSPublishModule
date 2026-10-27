namespace PowerForge.Tests;

public sealed class PSResourceGetInstallDiagnosticsTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Install_and_save_preserve_the_provider_cause_before_its_generic_failure(bool save, bool transient)
    {
        using var root = new TemporaryDirectory();
        var hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
        foreach (var host in hosts)
        {
            var runner = new ProviderFixtureRunner(root.Path, transient, host);
            var client = new PSResourceGetClient(runner, new NullLogger());
            void Install()
            {
                if (save) client.Save(new PSResourceSaveOptions("PSUnifi", root.Path, version: "1.0.0"));
                else client.Install(new PSResourceInstallOptions("PSUnifi", version: "1.0.0"));
            }

            if (transient)
            {
                Install();
                Assert.Equal(2, runner.Calls);
            }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(Install);
                Assert.Contains("Destination file not found", error.Message);
                Assert.Equal(1, runner.Calls);
            }
        }
    }

    private sealed class ProviderFixtureRunner(string root, bool transient, string host) : IPowerShellRunner
    {
        public int Calls { get; private set; }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            Calls++;
            var fixture = """
                function Import-Module { param($Name, $ErrorAction) }
                function Invoke-ProviderFixture {
                  [CmdletBinding()]
                  param($Name, $Version, $Repository, $Path, $Scope,
                    [switch]$TrustRepository, [switch]$SkipDependencyCheck, [switch]$AcceptLicense,
                    [switch]$Quiet, [switch]$PassThru, [switch]$Reinstall, [switch]$Prerelease)
                  if (__FAIL__) {
                    Write-Verbose '__CAUSE__'
                    throw "Package(s) 'PSUnifi' could not be installed from repository 'PSGallery'."
                  }
                  [pscustomobject]@{ Name = 'PSUnifi'; Version = '1.0.0' }
                }
                Set-Alias Save-PSResource Invoke-ProviderFixture
                Set-Alias Install-PSResource Invoke-ProviderFixture
                """.Replace("__FAIL__", !transient || Calls == 1 ? "$true" : "$false")
                    .Replace("__CAUSE__", transient
                        ? "Response returned status code package: Not Found."
                        : "Destination file not found: C:\\modules\\404\\PSUnifi.psd1");
            var source = File.ReadAllText(request.ScriptPath!);
            var scriptPath = Path.Combine(root, "provider.ps1");
            File.WriteAllText(scriptPath, source.Replace("$ErrorActionPreference = 'Stop'", "$ErrorActionPreference = 'Stop'\n" + fixture));
            return new PowerShellRunner().Run(new PowerShellRunRequest(scriptPath, request.Arguments,
                TimeSpan.FromSeconds(30), workingDirectory: root, executableOverride: host));
        }
    }
}
