using PowerForge;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Tests;

/// <summary>
/// The signing commands come from a module file, as the real Microsoft.PowerShell.Security
/// cmdlets do, so worker runspaces load the same fake commands the calling runspace uses.
/// Each fake call records its start and end time; no private key or timestamp server is used.
/// </summary>
public sealed class AuthenticodeParallelSigningTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SignFiles_SignsFirstFileAloneThenUpToTheRequestedParallelism(int parallelism)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new SigningModuleFixture(failFile: null);
        var files = fixture.CreateFiles("One.ps1", "Two.ps1", "Three.ps1", "Four.ps1", "Five.ps1");

        var result = fixture.Sign(files, parallelism);

        Assert.False(result.HadErrors, result.Errors);
        Assert.Equal(files.Length, result.Signatures.Count);
        var calls = fixture.ReadCalls();
        Assert.Equal(files.OrderBy(file => file), calls.Select(call => call.File).OrderBy(file => file));
        var first = Assert.Single(calls, call => call.File == files[0]);
        Assert.All(calls.Where(call => call != first), call => Assert.True(call.Start >= first.End, $"{call.File} started before the first file finished."));
        var overlapping = calls.Any(call => calls.Any(other => other != call && other.Start < call.End && call.Start < other.End));
        Assert.Equal(parallelism > 1, overlapping);
    }

    [Fact]
    public void SignFiles_FailureStopsNewFilesAndReportsTheFailedFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new SigningModuleFixture(failFile: "Two.ps1");
        var files = fixture.CreateFiles("One.ps1", "Two.ps1", "Three.ps1", "Four.ps1", "Five.ps1", "Six.ps1");

        var result = fixture.Sign(files, parallelism: 2);

        Assert.True(result.HadErrors);
        Assert.Contains("Two.ps1", result.Errors, StringComparison.Ordinal);
        var started = fixture.ReadCalls().Select(call => Path.GetFileName(call.File)).ToArray();
        Assert.Contains("One.ps1", started);
        Assert.DoesNotContain("Four.ps1", started);
        Assert.DoesNotContain("Five.ps1", started);
        Assert.DoesNotContain("Six.ps1", started);
    }

    private sealed class SigningModuleFixture : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        private readonly RSA _key = RSA.Create(2048);
        private readonly X509Certificate2 _certificate;
        private readonly string _modulePath;
        private readonly string _logDirectory;

        internal SigningModuleFixture(string? failFile)
        {
            var request = new CertificateRequest("CN=Parallel signing fixture", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false));
            _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            _logDirectory = Directory.CreateDirectory(Path.Combine(_root.FullName, "calls")).FullName;
            _modulePath = Path.Combine(_root.FullName, "FakeAuthenticode.psm1");
            File.WriteAllText(_modulePath, $$"""
                function Get-AuthenticodeSignature {
                    param([string[]]$FilePath)
                    foreach ($file in $FilePath) { [pscustomobject]@{ Path = $file; Status = 'NotSigned' } }
                }
                function Set-AuthenticodeSignature {
                    param([Alias('LiteralPath')][string]$FilePath, $Certificate, $TimestampServer, $IncludeChain, $HashAlgorithm, [switch]$Force)
                    $name = [IO.Path]::GetFileName($FilePath)
                    $log = Join-Path '{{_logDirectory}}' $name
                    [IO.File]::WriteAllText($log + '.start', [DateTime]::UtcNow.Ticks.ToString())
                    if ($name -eq '{{failFile}}') { throw "Signing denied for $name" }
                    [Threading.Thread]::Sleep(300)
                    [IO.File]::WriteAllText($log + '.end', [DateTime]::UtcNow.Ticks.ToString())
                    [pscustomobject]@{ Path = $FilePath; Status = 'Valid' }
                }
                Export-ModuleMember -Function Get-AuthenticodeSignature, Set-AuthenticodeSignature
                """);
        }

        internal string[] CreateFiles(params string[] names)
            => names.Select(name =>
            {
                var path = Path.Combine(_root.FullName, name);
                File.WriteAllText(path, "# fixture");
                return path;
            }).ToArray();

        internal (bool HadErrors, string Errors, IReadOnlyList<PSObject> Signatures) Sign(string[] files, int parallelism)
        {
            var state = InitialSessionState.CreateDefault();
            // In-memory session policy only, so the unsigned fixture module loads in the test host.
            state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
            state.ImportPSModule(new[] { _modulePath });
            using var runspace = RunspaceFactory.CreateRunspace(state);
            runspace.Open();
            runspace.SessionStateProxy.SetVariable("Service", new AuthenticodeSigningService(new NullLogger()));
            runspace.SessionStateProxy.SetVariable("Request", new AuthenticodeSignRequest
            {
                Certificate = _certificate,
                FilePaths = files,
                TimeStampServer = "https://timestamp.example.test",
                MaxDegreeOfParallelism = parallelism
            });
            using var ps = PowerShell.Create();
            ps.Runspace = runspace;
            ps.AddScript("$Service.SignFiles($Request)");
            var result = ps.Invoke();
            return (ps.HadErrors, string.Join(Environment.NewLine, ps.Streams.Error), result.ToArray());
        }

        internal IReadOnlyList<(string File, long Start, long End)> ReadCalls()
            => Directory.GetFiles(_logDirectory, "*.start")
                .Select(start =>
                {
                    var name = Path.GetFileNameWithoutExtension(start);
                    var end = Path.Combine(_logDirectory, name + ".end");
                    return (
                        File: Path.Combine(_root.FullName, name),
                        Start: long.Parse(File.ReadAllText(start)),
                        End: File.Exists(end) ? long.Parse(File.ReadAllText(end)) : long.MaxValue);
                })
                .ToArray();

        public void Dispose()
        {
            _certificate.Dispose();
            _key.Dispose();
            try { _root.Delete(true); } catch { /* best effort */ }
        }
    }
}
