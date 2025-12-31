using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security.Cryptography.X509Certificates;

namespace PSPublishModule;

/// <summary>
/// Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).
/// </summary>
[Cmdlet(VerbsLifecycle.Register, "Certificate", SupportsShouldProcess = true, DefaultParameterSetName = ParameterSetStore)]
public sealed class RegisterCertificateCommand : PSCmdlet
{
    private const string ParameterSetPfx = "PFX";
    private const string ParameterSetStore = "Store";

    /// <summary>A PFX file to use for signing (mutually exclusive with <c>-LocalStore</c>/<c>-Thumbprint</c>).</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetPfx)]
    [ValidateNotNullOrEmpty]
    public string CertificatePFX { get; set; } = string.Empty;

    /// <summary>Certificate store to search when using a certificate from the store.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetStore)]
    public CertificateStoreLocation LocalStore { get; set; }

    /// <summary>Certificate thumbprint to select a single certificate from the chosen store.</summary>
    [Alias("CertificateThumbprint")]
    [Parameter(ParameterSetName = ParameterSetStore)]
    public string? Thumbprint { get; set; }

    /// <summary>Root directory containing files to sign.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>RFC3161 timestamp server URL. Default: http://timestamp.digicert.com.</summary>
    [Parameter]
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Which portion of the chain to include in the signature. Default: All.</summary>
    [Parameter]
    public CertificateChainInclude IncludeChain { get; set; } = CertificateChainInclude.All;

    /// <summary>File patterns to include during signing. Default: scripts only.</summary>
    [Parameter]
    public string[] Include { get; set; } = { "*.ps1", "*.psd1", "*.psm1" };

    /// <summary>One or more path substrings to exclude from signing.</summary>
    [Parameter]
    public string[]? ExcludePath { get; set; }

    /// <summary>Hash algorithm used for the signature. Default: SHA256.</summary>
    [Parameter]
    public CertificateHashAlgorithm HashAlgorithm { get; set; } = CertificateHashAlgorithm.SHA256;

    /// <summary>Executes signing and outputs signature objects.</summary>
    protected override void ProcessRecord()
    {
        var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        if (!Directory.Exists(root))
        {
            WriteWarning($"Register-Certificate - Path '{root}' not found.");
            return;
        }

        List<X509Certificate2>? available = null;
        var certificate = ParameterSetName == ParameterSetPfx
            ? TryLoadPfx(CertificatePFX)
            : TrySelectFromStore(LocalStore, Thumbprint, out available);

        if (ParameterSetName == ParameterSetStore && certificate is null && available is not null && available.Count > 1 &&
            string.IsNullOrWhiteSpace(Thumbprint))
        {
            var codeError = $"Get-ChildItem -Path Cert:\\{LocalStore}\\My -CodeSigningCert";
            WriteWarning("Register-Certificate - More than one certificate found in store. Provide Thumbprint for expected certificate");
            WriteWarning($"Register-Certificate - Use: {codeError}");
            foreach (var c in available) WriteObject(c);
            return;
        }

        if (certificate is null)
        {
            WriteWarning("Register-Certificate - No certificates found.");
            return;
        }

        var files = EnumerateFiles(root, Include, ExcludePath).ToArray();
        if (files.Length == 0) return;

        if (IsWindows())
        {
            if (!HasCommand("Set-AuthenticodeSignature"))
            {
                WriteWarning("Register-Certificate - Code signing commands not found. Skipping signing.");
                return;
            }

            var sb = ScriptBlock.Create(@"
param($files,$cert,$ts,$includeChain,$hash)
$files |
  Where-Object { (Get-AuthenticodeSignature -FilePath $_).Status -eq 'NotSigned' } |
  ForEach-Object { Set-AuthenticodeSignature -FilePath $_ -Certificate $cert -TimestampServer $ts -IncludeChain $includeChain -HashAlgorithm $hash }
");
            var result = InvokeInModuleScope(sb, files, certificate, TimeStampServer, IncludeChain.ToString(), HashAlgorithm.ToString());
            WriteObject(result, enumerateCollection: true);
        }
        else
        {
            if (!HasCommand("Set-OpenAuthenticodeSignature"))
            {
                WriteWarning("Register-Certificate - OpenAuthenticode module not found. Please install it from PSGallery");
                return;
            }

            var includeOption = IncludeChain switch
            {
                CertificateChainInclude.All => "WholeChain",
                CertificateChainInclude.NotRoot => "ExcludeRoot",
                CertificateChainInclude.Signer => "EndCertOnly",
                _ => "None"
            };

            var sb = ScriptBlock.Create(@"
param($files,$cert,$ts,$includeChain,$hash)
$files |
  Where-Object { (Get-OpenAuthenticodeSignature -FilePath $_).Status -eq 'NotSigned' } |
  ForEach-Object { Set-OpenAuthenticodeSignature -FilePath $_ -Certificate $cert -TimeStampServer $ts -IncludeChain $includeChain -HashAlgorithm $hash }
");
            var result = InvokeInModuleScope(sb, files, certificate, TimeStampServer, includeOption, HashAlgorithm.ToString());
            WriteObject(result, enumerateCollection: true);
        }
    }

    private X509Certificate2? TryLoadPfx(string pfxPath)
    {
        var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(pfxPath);
        if (!File.Exists(resolved))
        {
            WriteWarning("Register-Certificate - PFX not found.");
            return null;
        }

        try
        {
            var flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet;
#if NET10_0_OR_GREATER
            var data = File.ReadAllBytes(resolved);
            return X509CertificateLoader.LoadPkcs12(data, (string?)null, flags);
#else
            return new X509Certificate2(resolved, (string?)null, flags);
#endif
        }
        catch (Exception ex)
        {
            WriteWarning($"Register-Certificate - No certificates found for PFX ({ex.Message})");
            return null;
        }
    }

    private static bool IsCodeSigningCert(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey) return false;
        foreach (var ext in cert.Extensions)
        {
            if (ext is X509EnhancedKeyUsageExtension eku)
            {
                foreach (var oid in eku.EnhancedKeyUsages)
                {
                    if (string.Equals(oid.Value, "1.3.6.1.5.5.7.3.3", StringComparison.Ordinal))
                        return true;
                }
            }
        }
        return false;
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static X509Certificate2? TrySelectFromStore(CertificateStoreLocation storeLocation, string? thumbprint, out List<X509Certificate2>? available)
    {
        available = null;
        try
        {
            var loc = storeLocation == CertificateStoreLocation.LocalMachine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, loc);
            store.Open(OpenFlags.ReadOnly);

            var certs = store.Certificates.Cast<X509Certificate2>().Where(IsCodeSigningCert).ToList();
            available = certs;

            if (certs.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                var norm = NormalizeThumbprint(thumbprint);
                return certs.FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == norm);
            }

            return certs.Count == 1 ? certs[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> EnumerateFiles(string root, IEnumerable<string> includePatterns, string[]? excludePath)
    {
        var includes = (includePatterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new WildcardPattern(p, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant))
            .ToArray();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var fileName = System.IO.Path.GetFileName(file);
            if (includes.Length > 0 && !includes.Any(p => p.IsMatch(fileName))) continue;

            // Always exclude Internals folder unless explicitly handled elsewhere in the pipeline.
            if (file.IndexOf($"{System.IO.Path.DirectorySeparatorChar}Internals{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                file.IndexOf("/Internals/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                file.IndexOf("\\Internals\\", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            if (excludePath is not null && excludePath.Length > 0)
            {
                var excluded = excludePath.Any(x => !string.IsNullOrWhiteSpace(x) &&
                    file.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
                if (excluded) continue;
            }

            yield return file;
        }
    }

    private bool HasCommand(string name)
    {
        try
        {
            return InvokeCommand.GetCommand(name, CommandTypes.Cmdlet | CommandTypes.Function | CommandTypes.Alias) is not null;
        }
        catch
        {
            return false;
        }
    }

    private ICollection<PSObject> InvokeInModuleScope(ScriptBlock scriptBlock, params object[] args)
    {
        // ModuleInfo.NewBoundScriptBlock works only for script modules. PSPublishModule cmdlets execute
        // in the binary module context, so we must invoke directly.
        return scriptBlock.Invoke(args);
    }

    private static bool IsWindows()
    {
#if NET472
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
#endif
    }
}
