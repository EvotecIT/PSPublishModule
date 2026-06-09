using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security.Cryptography.X509Certificates;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).
/// </summary>
/// <remarks>
/// <para>
/// Signs PowerShell scripts/manifests (and optionally binaries) using Authenticode.
/// When running in CI, prefer using a certificate from the Windows certificate store and referencing it by thumbprint.
/// </para>
/// </remarks>
/// <example>
/// <summary>Sign a module using a certificate from the current user store</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Register-Certificate -Path 'C:\Git\MyModule\Module' -LocalStore CurrentUser -Thumbprint '0123456789ABCDEF' -WhatIf</code>
/// <para>Previews which files would be signed.</para>
/// </example>
/// <example>
/// <summary>Sign using a PFX file</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Register-Certificate -CertificatePFX 'C:\Secrets\codesign.pfx' -Path 'C:\Git\MyModule\Module' -Include '*.ps1','*.psm1','*.psd1'</code>
/// <para>Uses a PFX directly (useful for local testing; store-based is recommended for CI).</para>
/// </example>
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
        var isVerbose = MyInvocation?.BoundParameters.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var signingService = new AuthenticodeSigningService(logger);
        var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        if (!Directory.Exists(root))
        {
            WriteWarning($"Register-Certificate - Path '{root}' not found.");
            return;
        }

        var lookup = ParameterSetName == ParameterSetPfx
            ? null
            : signingService.SelectCertificateFromStore(
                LocalStore == CertificateStoreLocation.LocalMachine
                    ? PowerForge.CertificateStoreLocation.LocalMachine
                    : PowerForge.CertificateStoreLocation.CurrentUser,
                Thumbprint);

        string? pfxError = null;
        var certificate = ParameterSetName == ParameterSetPfx
            ? signingService.TryLoadPfx(CertificatePFX, out pfxError)
            : lookup?.Certificate;

        if (ParameterSetName == ParameterSetPfx && certificate is null)
        {
            WriteWarning($"Register-Certificate - {pfxError}");
            return;
        }

        if (ParameterSetName == ParameterSetStore && certificate is null && lookup is not null && lookup.AvailableCertificates.Count > 1 &&
            string.IsNullOrWhiteSpace(Thumbprint))
        {
            var codeError = $"Get-ChildItem -Path Cert:\\{LocalStore}\\My -CodeSigningCert";
            WriteWarning("Register-Certificate - More than one certificate found in store. Provide Thumbprint for expected certificate");
            WriteWarning($"Register-Certificate - Use: {codeError}");
            foreach (var c in lookup.AvailableCertificates) WriteObject(c);
            return;
        }

        if (certificate is null)
        {
            WriteWarning("Register-Certificate - No certificates found.");
            return;
        }

        var files = signingService.EnumerateFiles(root, Include, ExcludePath);
        if (files.Length == 0) return;

        try
        {
            var result = signingService.SignFiles(new AuthenticodeSignRequest
            {
                Certificate = certificate,
                FilePaths = files,
                TimeStampServer = TimeStampServer,
                HashAlgorithm = HashAlgorithm.ToString(),
                WindowsIncludeChain = IncludeChain.ToString(),
                NonWindowsIncludeChain = IncludeChain switch
                {
                    CertificateChainInclude.All => "WholeChain",
                    CertificateChainInclude.NotRoot => "ExcludeRoot",
                    CertificateChainInclude.Signer => "EndCertOnly",
                    _ => "None"
                }
            });
            WriteObject(result, enumerateCollection: true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Set-AuthenticodeSignature", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("Get-AuthenticodeSignature", StringComparison.OrdinalIgnoreCase))
        {
            WriteWarning("Register-Certificate - Code signing commands not found. Skipping signing.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Set-OpenAuthenticodeSignature", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("Get-OpenAuthenticodeSignature", StringComparison.OrdinalIgnoreCase))
        {
            WriteWarning("Register-Certificate - OpenAuthenticode module not found. Please install it from PSGallery");
        }
        catch (Exception ex)
        {
            WriteWarning($"Register-Certificate - Signing failed ({ex.Message})");
        }
    }
}
