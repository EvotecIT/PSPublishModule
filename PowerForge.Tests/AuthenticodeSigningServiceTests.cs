using PowerForge;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Tests;

public sealed class AuthenticodeSigningServiceTests
{
    [Theory]
    [InlineData("Valid", false)]
    [InlineData("UnknownError", false)]
    [InlineData("Throw", false)]
    [InlineData("NoResult", false)]
    [InlineData("UnknownError", true)]
    public void SignFiles_ValidatesEachFileBeforeStartingTheNext(string secondResult, bool overwrite)
    {
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Signing orchestration fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false));
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var files = new[] { "Existing.ps1", "One.ps1", "Two.ps1", "Three.ps1" }.Select(Path.GetFullPath).ToArray();
        var calls = new List<string>();
        var state = InitialSessionState.CreateDefault();
        var suffix = OperatingSystem.IsWindows() ? "AuthenticodeSignature" : "OpenAuthenticodeSignature";
        state.Commands.Add(new SessionStateFunctionEntry("Get-" + suffix, """
            param([string[]]$FilePath)
            foreach ($file in $FilePath) {
                [pscustomobject]@{ Path = $file; Status = $(if ([IO.Path]::GetFileName($file) -eq 'Existing.ps1') { 'Valid' } else { 'NotSigned' }) }
            }
            """));
        state.Commands.Add(new SessionStateFunctionEntry("Set-" + suffix, """
            param([string[]]$FilePath, $Certificate, $TimestampServer, $IncludeChain, $HashAlgorithm, [switch]$Force)
            foreach ($file in $FilePath) {
                $Calls.Add($file)
                $status = 'Valid'
                if ([IO.Path]::GetFileName($file) -eq 'Two.ps1') {
                    if ($SecondResult -eq 'Throw') { throw 'Signing denied' }
                    if ($SecondResult -eq 'NoResult') { continue }
                    $status = $SecondResult
                }
                [pscustomobject]@{ Path = $file; Status = $status; StatusMessage = 'Synthetic signing result' }
            }
            """));
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        runspace.SessionStateProxy.SetVariable("Calls", calls);
        runspace.SessionStateProxy.SetVariable("SecondResult", secondResult);
        runspace.SessionStateProxy.SetVariable("Service", new AuthenticodeSigningService(new NullLogger()));
        runspace.SessionStateProxy.SetVariable("Request", new AuthenticodeSignRequest { Certificate = certificate, FilePaths = files, OverwriteSigned = overwrite });
        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddScript("$Service.SignFiles($Request)");
        var result = ps.Invoke();

        var targets = overwrite ? files : files.Skip(1).ToArray();
        if (secondResult == "Valid")
        {
            Assert.False(ps.HadErrors, string.Join(Environment.NewLine, ps.Streams.Error));
            Assert.Equal(targets, calls);
            Assert.Equal(targets.Length, result.Count);
            Assert.All(result, signature => Assert.Equal("Valid", signature.Properties["Status"].Value));
        }
        else
        {
            Assert.True(ps.HadErrors);
            Assert.Equal(targets.Take(targets.Length - 1), calls);
            Assert.Contains(secondResult == "Throw" ? "Signing denied" : "Two.ps1", ps.Streams.Error[0].ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnumerateFiles_FiltersByPattern_AndSkipsInternalsAndExcludedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-signing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var publicDir = Directory.CreateDirectory(Path.Combine(root, "Public"));
            var internalsDir = Directory.CreateDirectory(Path.Combine(root, "Internals"));
            var excludedDir = Directory.CreateDirectory(Path.Combine(root, "IgnoreMe"));

            var publicScript = Path.Combine(publicDir.FullName, "Invoke-Test.ps1");
            var publicManifest = Path.Combine(publicDir.FullName, "Module.psd1");
            var internalScript = Path.Combine(internalsDir.FullName, "Hidden.ps1");
            var excludedScript = Path.Combine(excludedDir.FullName, "Skip.ps1");
            var textFile = Path.Combine(publicDir.FullName, "notes.txt");

            File.WriteAllText(publicScript, "Write-Host test");
            File.WriteAllText(publicManifest, "@{}");
            File.WriteAllText(internalScript, "Write-Host hidden");
            File.WriteAllText(excludedScript, "Write-Host excluded");
            File.WriteAllText(textFile, "nope");

            var service = new AuthenticodeSigningService(new NullLogger());
            var files = service.EnumerateFiles(root, new[] { "*.ps1", "*.psd1" }, new[] { "IgnoreMe" });

            Assert.Contains(publicScript, files, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(publicManifest, files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(internalScript, files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(excludedScript, files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(textFile, files, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
