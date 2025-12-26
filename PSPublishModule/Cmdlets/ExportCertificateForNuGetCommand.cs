using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace PSPublishModule;

/// <summary>
/// Exports a code-signing certificate to DER format for NuGet.org registration.
/// </summary>
[Cmdlet(VerbsData.Export, "CertificateForNuGet", DefaultParameterSetName = ParameterSetThumbprint)]
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
        X509Store? store = null;
        try
        {
            var location = LocalStore == CertificateStoreLocation.LocalMachine
                ? StoreLocation.LocalMachine
                : StoreLocation.CurrentUser;

            store = new X509Store("My", location);
            store.Open(OpenFlags.ReadOnly);

            var cert = FindCertificate(store);
            if (cert is null)
                throw new InvalidOperationException(BuildNotFoundMessage());

            if (!HasCodeSigningEku(cert))
            {
                WriteWarning("Certificate does not appear to have Code Signing capability. This may not work for NuGet package signing.");
            }

            var output = ResolveOutputPath(cert);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output) ?? ".");

            var bytes = cert.Export(X509ContentType.Cert);
            File.WriteAllBytes(output, bytes);

            HostWriteLineSafe($"Certificate exported successfully to: {output}", ConsoleColor.Green);
            HostWriteLineSafe(string.Empty);
            HostWriteLineSafe("Next steps to register with NuGet.org:", ConsoleColor.Yellow);
            HostWriteLineSafe("1. Go to https://www.nuget.org and sign in");
            HostWriteLineSafe("2. Go to Account Settings > Certificates");
            HostWriteLineSafe("3. Click 'Register new'");
            HostWriteLineSafe($"4. Upload the file: {output}");
            HostWriteLineSafe("5. Once registered, all future packages must be signed with this certificate");
            HostWriteLineSafe(string.Empty);
            HostWriteLineSafe("Certificate details:", ConsoleColor.Cyan);
            HostWriteLineSafe($"  Subject: {cert.Subject}");
            HostWriteLineSafe($"  Issuer: {cert.Issuer}");
            HostWriteLineSafe($"  Thumbprint: {cert.Thumbprint}");
            HostWriteLineSafe($"  SHA256: {GetSha256Hex(cert)}");
            HostWriteLineSafe($"  Valid From: {cert.NotBefore}");
            HostWriteLineSafe($"  Valid To: {cert.NotAfter}");

            var ok = new PSObject();
            ok.Properties.Add(new PSNoteProperty("Success", true));
            ok.Properties.Add(new PSNoteProperty("CertificatePath", output));
            ok.Properties.Add(new PSNoteProperty("Certificate", cert));
            WriteObject(ok);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "ExportCertificateForNuGetFailed", ErrorCategory.NotSpecified, null));
            var fail = new PSObject();
            fail.Properties.Add(new PSNoteProperty("Success", false));
            fail.Properties.Add(new PSNoteProperty("Error", ex.Message));
            WriteObject(fail);
        }
        finally
        {
            try { store?.Close(); }
            catch { /* ignore */ }
        }
    }

    private X509Certificate2? FindCertificate(X509Store store)
    {
        if (ParameterSetName == ParameterSetThumbprint)
        {
            var thumb = (CertificateThumbprint ?? string.Empty).Replace(" ", string.Empty);
            return store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(c => string.Equals(c.Thumbprint, thumb, StringComparison.OrdinalIgnoreCase));
        }

        var sha = (CertificateSha256 ?? string.Empty).Replace(" ", string.Empty);
        return store.Certificates.Cast<X509Certificate2>()
            .FirstOrDefault(c => string.Equals(GetSha256Hex(c), sha, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildNotFoundMessage()
    {
        if (ParameterSetName == ParameterSetThumbprint)
            return $"Certificate with thumbprint '{CertificateThumbprint}' not found in {LocalStore}\\My store";
        return $"Certificate with SHA256 '{CertificateSha256}' not found in {LocalStore}\\My store";
    }

    private string ResolveOutputPath(X509Certificate2 cert)
    {
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputPath);
        }

        var first = (cert.Subject ?? string.Empty).Split(',').FirstOrDefault() ?? string.Empty;
        first = first.Trim();
        if (first.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            first = first.Substring(3);

        var subjectName = Regex.Replace(first, @"[^\w\s-]", string.Empty);
        if (string.IsNullOrWhiteSpace(subjectName))
            subjectName = "Certificate";

        var fileName = $"{subjectName}-CodeSigning.cer";
        var cwd = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory();
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(cwd, fileName));
    }

    private static bool HasCodeSigningEku(X509Certificate2 cert)
    {
        const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";

        foreach (var ext in cert.Extensions)
        {
            if (ext is X509EnhancedKeyUsageExtension eku)
            {
                foreach (var oid in eku.EnhancedKeyUsages)
                {
                    if (string.Equals(oid.Value, CodeSigningOid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(oid.FriendlyName, "Code Signing", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string GetSha256Hex(X509Certificate2 cert)
    {
#if NET8_0_OR_GREATER
        return cert.GetCertHashString(HashAlgorithmName.SHA256);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(cert.RawData);
        return BitConverter.ToString(hash).Replace("-", string.Empty);
#endif
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
