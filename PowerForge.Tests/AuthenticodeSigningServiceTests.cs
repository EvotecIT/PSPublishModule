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
    [InlineData("Valid", true)]
    [InlineData("UnknownError", false)]
    [InlineData("Throw", false)]
    [InlineData("WriteError", false)]
    [InlineData("NoResult", false)]
    [InlineData("NoSignature", false)]
    [InlineData("MissingCertificate", false)]
    [InlineData("WrongCertificate", false)]
    [InlineData("PrecheckError", false)]
    [InlineData("UnknownError", true)]
    public void SignFiles_ValidatesEachFileBeforeStartingTheNext(string secondResult, bool overwrite)
    {
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Signing orchestration fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false));
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var vendorKey = RSA.Create(2048);
        var vendorRequest = new CertificateRequest("CN=Existing vendor", vendorKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var vendorCertificate = vendorRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var files = new[] { "Existing.ps1", "One.ps1", "Two.ps1", "Three.ps1" }.Select(Path.GetFullPath).ToArray();
        var calls = new List<string>();
        var events = new List<string>();
        var state = InitialSessionState.CreateDefault();
        if (OperatingSystem.IsWindows())
        {
            state.Commands.Add(new SessionStateFunctionEntry("Get-AuthenticodeSignature", """
            param([string[]]$FilePath)
            foreach ($file in $FilePath) {
                if ($SecondResult -eq 'PrecheckError' -and [IO.Path]::GetFileName($file) -eq 'Two.ps1') { throw 'Read denied' }
                [pscustomobject]@{ Path = $file; Status = $(if ([IO.Path]::GetFileName($file) -eq 'Existing.ps1') { 'Valid' } else { 'NotSigned' }) }
            }
            """));
            state.Commands.Add(new SessionStateFunctionEntry("Set-AuthenticodeSignature", """
            param([string[]]$FilePath, $Certificate, $TimestampServer, $IncludeChain, $HashAlgorithm, [switch]$Force)
            foreach ($file in $FilePath) {
                $Calls.Add($file)
                $status = 'Valid'
                if ([IO.Path]::GetFileName($file) -eq 'Two.ps1') {
                    if ($SecondResult -eq 'Throw') { throw 'Signing denied' }
                    if ($SecondResult -eq 'NoResult') { continue }
                    $status = if ($SecondResult -in @('WriteError', 'NoSignature', 'MissingCertificate', 'WrongCertificate')) { 'UnknownError' } else { $SecondResult }
                }
                [pscustomobject]@{ Path = $file; Status = $status; StatusMessage = 'Synthetic signing result' }
            }
            """));
        }
        else
        {
            state.Commands.Add(new SessionStateFunctionEntry("Get-OpenAuthenticodeSignature", """
            [CmdletBinding()]
            param([Parameter(Mandatory)][string[]]$LiteralPath)
            if ($LiteralPath.Count -ne 1) { throw 'Expected one literal path' }
            $file = $LiteralPath[0]
            $Events.Add("Get:$file")
            $isSecond = [IO.Path]::GetFileName($file) -eq 'Two.ps1'
            $written = $Calls.Contains($file)
            if ($isSecond -and (($written -and $SecondResult -eq 'UnknownError') -or $SecondResult -eq 'PrecheckError')) {
                $PSCmdlet.WriteError([System.Management.Automation.ErrorRecord]::new(
                    [InvalidOperationException]::new('Read denied'), 'GetSignatureError', 'NotSpecified', $file))
                return
            }
            if ($isSecond -and $written -and $SecondResult -eq 'NoResult') { return }
            if ((-not $written -and [IO.Path]::GetFileName($file) -ne 'Existing.ps1') -or
                ($isSecond -and $written -and $SecondResult -eq 'NoSignature')) {
                $PSCmdlet.WriteError([System.Management.Automation.ErrorRecord]::new(
                    [System.Management.Automation.ItemNotFoundException]::new("File '$file' does not contain an authenticode signature"),
                    'NoSignature', 'ObjectNotFound', $file))
                return
            }
            $signer = if ($written) { $SigningCertificate } else { $VendorCertificate }
            if ($isSecond -and $written -and $SecondResult -eq 'MissingCertificate') { $signer = $null }
            if ($isSecond -and $written -and $SecondResult -eq 'WrongCertificate') { $signer = $VendorCertificate }
            $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new()
            Add-Member -InputObject $cms -NotePropertyMembers @{ Path = $file; Certificate = $signer } -PassThru
            """));
            state.Commands.Add(new SessionStateFunctionEntry("Set-OpenAuthenticodeSignature", """
            [CmdletBinding()]
            param(
                [Parameter(Mandatory)][string[]]$LiteralPath,
                [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
                [string]$TimeStampServer,
                [System.Security.Cryptography.X509Certificates.X509IncludeOption]$IncludeOption,
                [System.Security.Cryptography.HashAlgorithmName]$HashAlgorithm
            )
            if ($LiteralPath.Count -ne 1) { throw 'Expected one literal path' }
            if ($IncludeOption -ne 'WholeChain' -or $HashAlgorithm.Name -ne 'SHA256' -or
                $TimeStampServer -ne 'https://timestamp.example.test' -or $Certificate.Thumbprint -ne $SigningCertificate.Thumbprint) {
                throw 'Incorrect signing configuration'
            }
            $file = $LiteralPath[0]
            $Calls.Add($file)
            $Events.Add("Set:$file")
            if ([IO.Path]::GetFileName($file) -eq 'Two.ps1' -and $SecondResult -eq 'Throw') { throw 'Signing denied' }
            if ([IO.Path]::GetFileName($file) -eq 'Two.ps1' -and $SecondResult -eq 'WriteError') {
                $PSCmdlet.WriteError([System.Management.Automation.ErrorRecord]::new(
                    [InvalidOperationException]::new("Signing denied for $file"), 'SetSignatureError', 'NotSpecified', $file))
            }
            # OpenAuthenticode writes no output unless PassThru is requested.
            """));
        }
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        runspace.SessionStateProxy.SetVariable("Calls", calls);
        runspace.SessionStateProxy.SetVariable("Events", events);
        runspace.SessionStateProxy.SetVariable("SecondResult", secondResult);
        runspace.SessionStateProxy.SetVariable("SigningCertificate", certificate);
        runspace.SessionStateProxy.SetVariable("VendorCertificate", vendorCertificate);
        runspace.SessionStateProxy.SetVariable("Service", new AuthenticodeSigningService(new NullLogger()));
        runspace.SessionStateProxy.SetVariable("Request", new AuthenticodeSignRequest
        {
            Certificate = certificate,
            FilePaths = files,
            OverwriteSigned = overwrite,
            TimeStampServer = "https://timestamp.example.test"
        });
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
            if (OperatingSystem.IsWindows())
                Assert.All(result, signature => Assert.Equal("Valid", signature.Properties["Status"].Value));
            else
            {
                Assert.All(result, signature =>
                {
                    Assert.IsType<System.Security.Cryptography.Pkcs.SignedCms>(signature.BaseObject);
                    Assert.Equal(certificate.Thumbprint, Assert.IsType<X509Certificate2>(signature.Properties["Certificate"].Value).Thumbprint);
                });
                Assert.Equal(targets, result.Select(signature => signature.Properties["Path"].Value?.ToString()));
            }
        }
        else
        {
            Assert.True(ps.HadErrors);
            var attempted = secondResult == "PrecheckError"
                ? (OperatingSystem.IsWindows() ? Array.Empty<string>() : targets.Take(1))
                : targets.Take(targets.Length - 1);
            Assert.Equal(attempted, calls);
            var expectedError = secondResult == "Throw" ? "Signing denied" :
                secondResult == "PrecheckError" && OperatingSystem.IsWindows() ? "Read denied" : "Two.ps1";
            Assert.Contains(expectedError, ps.Streams.Error[0].ToString(), StringComparison.Ordinal);
        }
        if (!OperatingSystem.IsWindows())
        {
            foreach (var file in calls)
            {
                var writeIndex = events.IndexOf("Set:" + file);
                if ((secondResult == "Throw" || secondResult == "WriteError") && file == files[2])
                    Assert.Equal(events.Count - 1, writeIndex);
                else
                    Assert.Equal("Get:" + file, events[writeIndex + 1]);
            }
            if (!overwrite)
                Assert.DoesNotContain(files[0], calls);
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
