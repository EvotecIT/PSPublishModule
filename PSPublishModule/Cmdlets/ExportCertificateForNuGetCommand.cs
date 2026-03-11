using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Exports a code-signing certificate to DER format for NuGet.org registration.
/// </summary>
/// <remarks>
/// <para>
/// NuGet.org requires uploading the public certificate (<c>.cer</c>) used for package signing.
/// This cmdlet exports the selected certificate from the local certificate store.
/// </para>
/// </remarks>
/// <example>
/// <summary>Export by thumbprint</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Export-CertificateForNuGet -CertificateThumbprint '0123456789ABCDEF' -OutputPath 'C:\Temp\NuGetSigning.cer'</code>
/// <para>Exports the certificate in DER format to the given path.</para>
/// </example>
/// <example>
/// <summary>Export by SHA256 hash</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Export-CertificateForNuGet -CertificateSha256 '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF'</code>
/// <para>Useful when you have the SHA256 fingerprint but not the Windows thumbprint.</para>
/// </example>
[Cmdlet(VerbsData.Export, "CertificateForNuGet", DefaultParameterSetName = ParameterSetThumbprint)]
[OutputType(typeof(NuGetCertificateExportResult))]
public sealed class ExportCertificateForNuGetCommand : PSCmdlet
{
    private const string ParameterSetThumbprint = "Thumbprint";
    private const string ParameterSetSha256 = "Sha256";

    /// <summary>The SHA1 thumbprint of the certificate to export.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetThumbprint)]
    [ValidateNotNullOrEmpty]
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>The SHA256 hash of the certificate to export.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetSha256)]
    [ValidateNotNullOrEmpty]
    public string CertificateSha256 { get; set; } = string.Empty;

    /// <summary>Output path for the exported .cer file.</summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>Certificate store location to use.</summary>
    [Parameter]
    public CertificateStoreLocation LocalStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Executes the export.</summary>
    protected override void ProcessRecord()
    {
        var storeLocation = LocalStore == CertificateStoreLocation.LocalMachine
            ? PowerForge.CertificateStoreLocation.LocalMachine
            : PowerForge.CertificateStoreLocation.CurrentUser;
        var currentDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory();
        var result = new NuGetCertificateExportService().Execute(new NuGetCertificateExportRequest
        {
            CertificateThumbprint = ParameterSetName == ParameterSetThumbprint ? CertificateThumbprint : null,
            CertificateSha256 = ParameterSetName == ParameterSetSha256 ? CertificateSha256 : null,
            OutputPath = OutputPath,
            StoreLocation = storeLocation,
            WorkingDirectory = currentDirectory
        });

        if (!result.Success)
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException(result.Error ?? "Certificate export failed."),
                "ExportCertificateForNuGetFailed",
                ErrorCategory.NotSpecified,
                null));
            WriteObject(result);
            return;
        }

        if (!result.HasCodeSigningEku)
        {
            WriteWarning("Certificate does not appear to have Code Signing capability. This may not work for NuGet package signing.");
        }

        HostWriteLineSafe($"Certificate exported successfully to: {result.CertificatePath}", ConsoleColor.Green);
        HostWriteLineSafe(string.Empty);
        HostWriteLineSafe("Next steps to register with NuGet.org:", ConsoleColor.Yellow);
        HostWriteLineSafe("1. Go to https://www.nuget.org and sign in");
        HostWriteLineSafe("2. Go to Account Settings > Certificates");
        HostWriteLineSafe("3. Click 'Register new'");
        HostWriteLineSafe($"4. Upload the file: {result.CertificatePath}");
        HostWriteLineSafe("5. Once registered, all future packages must be signed with this certificate");
        HostWriteLineSafe(string.Empty);
        HostWriteLineSafe("Certificate details:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Subject: {result.Subject}");
        HostWriteLineSafe($"  Issuer: {result.Issuer}");
        HostWriteLineSafe($"  Thumbprint: {result.Certificate?.Thumbprint}");
        HostWriteLineSafe($"  SHA256: {result.Sha256}");
        HostWriteLineSafe($"  Valid From: {result.NotBefore}");
        HostWriteLineSafe($"  Valid To: {result.NotAfter}");

        WriteObject(result);
    }

    private void HostWriteLineSafe(string text, ConsoleColor? fg = null)
    {
        try
        {
            if (fg.HasValue)
            {
                var bg = Host?.UI?.RawUI?.BackgroundColor ?? ConsoleColor.Black;
                Host?.UI?.WriteLine(fg.Value, bg, text);
            }
            else
            {
                Host?.UI?.WriteLine(text);
            }
        }
        catch
        {
            // ignore host limitations
        }
    }
}
