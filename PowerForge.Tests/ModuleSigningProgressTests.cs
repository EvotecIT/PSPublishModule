using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ModuleSigningProgressTests
{
    [Fact]
    public void SigningScript_StreamsCertificatePrecheckRetryAndFileMilestones()
    {
        if (Path.DirectorySeparatorChar != '\\') return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var files = new[] { Path.Combine(root.FullName, "One.psm1"), Path.Combine(root.FullName, "Two.ps1") };
            foreach (var file in files) File.WriteAllText(file, "# signing progress fixture");
            var updates = new List<string>();
            var runner = new CallbackRunner(request =>
            {
                // Replace only external certificate/provider operations in the actual signing script.
                // No certificate store lookup, private key, network timestamp or real signing can run.
                const string fixtures = """
                function Get-Item {
                  [CmdletBinding()] param([string]$LiteralPath)
                  if ($LiteralPath -notlike 'Cert:*') { throw 'Unexpected metadata lookup' }
                  [pscustomobject]@{ HasPrivateKey=$true; Extensions=@(); Thumbprint='ABC'; NotBefore=[DateTime]::Now.AddDays(-1); NotAfter=[DateTime]::Now.AddDays(1) }
                }
                function Get-AuthenticodeSignature {
                  [CmdletBinding()] param([string]$FilePath)
                  [pscustomobject]@{ Status='NotSigned'; SignerCertificate=$null }
                }
                $script:calls=0
                function Set-AuthenticodeSignature {
                  [CmdletBinding()] param([string]$FilePath,[object]$Certificate,[string]$TimestampServer,[object]$IncludeChain,[string]$HashAlgorithm,[switch]$Force)
                  $script:calls++
                  [pscustomobject]@{Status=$(if($script:calls -eq 1){'UnknownError'}else{'Valid'});StatusMessage='fixture'}
                }
                function Start-Sleep { param([int]$Milliseconds) }
                """;
                var script = File.ReadAllText(request.ScriptPath!);
                script = script.Replace("$ErrorActionPreference = 'Stop'", fixtures + Environment.NewLine + "$ErrorActionPreference = 'Stop'");
                File.WriteAllText(request.ScriptPath!, script, new UTF8Encoding(true));
                return new PowerShellRunner().Run(request);
            });
            var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger(), isWindows: true);
            var result = operations.SignModuleOutput("Demo", root.FullName, files, ["*.psm1", "*.ps1"], [],
                new SigningOptionsConfiguration { CertificateThumbprint = "ABC" },
                (_, _, detail) => updates.Add(detail));
            Assert.Equal(2, result.SignedNew);
            Assert.Contains(updates, detail => detail.StartsWith("Loading signing certificate"));
            Assert.Contains(updates, detail => detail.StartsWith("Checking existing signatures 2/2"));
            Assert.Contains(updates, detail => detail.Contains("retry 2/15"));
            Assert.Contains(updates, detail => detail == "Signing and timestamping 2/2: Two.ps1");
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Signing_StreamsMilestonesBeforeTheRequiredHostReturns()
    {
        var updates = new List<(int Completed, int Total, string Detail)>();
        var runner = new CallbackRunner(request =>
        {
            Assert.Equal(PowerShellHostRequirement.WindowsPowerShell, request.HostRequirement);
            Assert.False(request.PreferPwsh);
            Assert.NotNull(request.OutputLineReceived);
            request.OutputLineReceived!(Line(0, 0, "Loading signing certificate"));
            request.OutputLineReceived(Line(0, 2, "Signing and timestamping 1/2: Test.psm1"));
            request.OutputLineReceived("PFSIGN::PROGRESS::bad");
            request.OutputLineReceived("ordinary output");
            Assert.Contains(updates, update => update.Detail.Contains("1/2"));
            var summary = new ModuleSigningResult { Attempted = 2, SignedNew = 2 };
            return new PowerShellRunResult(0,
                "PFSIGN::SUMMARY::" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary))),
                string.Empty, "powershell.exe");
        });
        var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger(), isWindows: true);
        var result = operations.SignModuleOutput("Demo", Path.GetTempPath(), ["Test.psm1"], ["*.psm1"], [],
            new SigningOptionsConfiguration { CertificateThumbprint = "synthetic-metadata-only" },
            (completed, total, detail) => updates.Add((completed, total, detail)));
        Assert.Equal(2, result.SignedNew);
        Assert.Contains(updates, update => update == (0, 2, "Signing and timestamping 1/2: Test.psm1"));
        Assert.DoesNotContain(updates, update => update.Detail.Contains("ordinary output"));
    }

    [Theory]
    [InlineData("PFSIGN::PROGRESS::-1:2:ZmlsZQ==")]
    [InlineData("PFSIGN::PROGRESS::3:2:ZmlsZQ==")]
    [InlineData("PFSIGN::PROGRESS::0:0:invalid base64")]
    public void Signing_RejectsMalformedMilestones(string line)
        => Assert.False(ModuleSigningProgressProtocol.TryParse(line, out _, out _, out _));

    private static string Line(int completed, int total, string detail)
        => $"PFSIGN::PROGRESS::{completed}:{total}:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(detail));

    private sealed class CallbackRunner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }
}
