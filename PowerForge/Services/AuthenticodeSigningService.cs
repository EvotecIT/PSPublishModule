using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Provides reusable Authenticode signing helpers for module and .NET release workflows.
/// </summary>
public sealed class AuthenticodeSigningService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new signing service.
    /// </summary>
    public AuthenticodeSigningService(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Loads a PFX certificate from disk.
    /// </summary>
    public X509Certificate2? TryLoadPfx(string pfxPath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(pfxPath))
        {
            errorMessage = "PFX path is required.";
            return null;
        }

        var resolved = Path.GetFullPath(pfxPath.Trim().Trim('"'));
        if (!File.Exists(resolved))
        {
            errorMessage = "PFX not found.";
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
            errorMessage = $"No certificates found for PFX ({ex.Message})";
            return null;
        }
    }

    /// <summary>
    /// Selects a code-signing certificate from the specified store.
    /// </summary>
    public CodeSigningCertificateLookupResult SelectCertificateFromStore(CertificateStoreLocation storeLocation, string? thumbprint)
    {
        try
        {
            var loc = storeLocation == CertificateStoreLocation.LocalMachine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, loc);
            store.Open(OpenFlags.ReadOnly);

            var certs = store.Certificates.Cast<X509Certificate2>().Where(IsCodeSigningCert).ToArray();
            if (certs.Length == 0)
            {
                return new CodeSigningCertificateLookupResult
                {
                    Certificate = null,
                    AvailableCertificates = Array.Empty<X509Certificate2>()
                };
            }

            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                var normalized = NormalizeThumbprint(thumbprint);
                return new CodeSigningCertificateLookupResult
                {
                    Certificate = certs.FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == normalized),
                    AvailableCertificates = certs
                };
            }

            return new CodeSigningCertificateLookupResult
            {
                Certificate = certs.Length == 1 ? certs[0] : null,
                AvailableCertificates = certs
            };
        }
        catch
        {
            return new CodeSigningCertificateLookupResult
            {
                Certificate = null,
                AvailableCertificates = Array.Empty<X509Certificate2>()
            };
        }
    }

    /// <summary>
    /// Enumerates files matching the supplied include patterns while excluding common internal paths.
    /// </summary>
    public string[] EnumerateFiles(string root, IEnumerable<string> includePatterns, string[]? excludePathSubstrings = null)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Root path is required.", nameof(root));

        var fullRoot = Path.GetFullPath(root.Trim().Trim('"'));
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"Path '{fullRoot}' not found.");

        var includes = (includePatterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new WildcardPattern(p, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant))
            .ToArray();

        var results = new List<string>();
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (includes.Length > 0 && !includes.Any(pattern => pattern.IsMatch(fileName)))
                continue;

            if (IsInternalsPath(file))
                continue;

            if (excludePathSubstrings is not null &&
                excludePathSubstrings.Any(x => !string.IsNullOrWhiteSpace(x) && file.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            results.Add(file);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Signs the requested files using the current PowerShell runspace when available.
    /// </summary>
    public IReadOnlyList<PSObject> SignFiles(AuthenticodeSignRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Certificate is null) throw new ArgumentException("Certificate is required.", nameof(request));

        var files = (request.FilePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return Array.Empty<PSObject>();

        if (IsWindows())
        {
            EnsureCommandAvailable("Get-AuthenticodeSignature");
            EnsureCommandAvailable("Set-AuthenticodeSignature");
            return SignWithWindowsAuthenticode(files, request);
        }

        EnsureCommandAvailable("Get-OpenAuthenticodeSignature");
        EnsureCommandAvailable("Set-OpenAuthenticodeSignature");
        return SignWithOpenAuthenticode(files, request);
    }

    private IReadOnlyList<PSObject> SignWithWindowsAuthenticode(string[] files, AuthenticodeSignRequest request)
    {
        var outputs = new List<PSObject>();

        foreach (var file in files)
        {
            var signature = InvokeSingle("Get-AuthenticodeSignature", ps =>
            {
                ps.AddParameter("FilePath", file);
            });

            var status = signature?.Properties["Status"]?.Value?.ToString();
            if (!string.Equals(status, "NotSigned", StringComparison.OrdinalIgnoreCase))
                continue;

            _logger.Verbose($"Signing file: {file}");
            var signed = InvokeSingle("Set-AuthenticodeSignature", ps =>
            {
                ps.AddParameter("FilePath", file);
                ps.AddParameter("Certificate", request.Certificate);
                ps.AddParameter("TimestampServer", request.TimeStampServer);
                ps.AddParameter("IncludeChain", request.WindowsIncludeChain);
                ps.AddParameter("HashAlgorithm", request.HashAlgorithm);
            });

            if (signed is not null)
                outputs.Add(signed);
        }

        return outputs;
    }

    private IReadOnlyList<PSObject> SignWithOpenAuthenticode(string[] files, AuthenticodeSignRequest request)
    {
        var outputs = new List<PSObject>();

        foreach (var file in files)
        {
            var signature = InvokeSingle("Get-OpenAuthenticodeSignature", ps =>
            {
                ps.AddParameter("FilePath", file);
            });

            var status = signature?.Properties["Status"]?.Value?.ToString();
            if (!string.Equals(status, "NotSigned", StringComparison.OrdinalIgnoreCase))
                continue;

            _logger.Verbose($"Signing file: {file}");
            var signed = InvokeSingle("Set-OpenAuthenticodeSignature", ps =>
            {
                ps.AddParameter("FilePath", file);
                ps.AddParameter("Certificate", request.Certificate);
                ps.AddParameter("TimeStampServer", request.TimeStampServer);
                ps.AddParameter("IncludeChain", request.NonWindowsIncludeChain);
                ps.AddParameter("HashAlgorithm", request.HashAlgorithm);
            });

            if (signed is not null)
                outputs.Add(signed);
        }

        return outputs;
    }

    private void EnsureCommandAvailable(string name)
    {
        using var ps = CreatePowerShell();
        ps.AddCommand("Get-Command").AddParameter("Name", name);
        var result = ps.Invoke();
        if (ps.HadErrors || result.Count == 0)
        {
            throw new InvalidOperationException($"Required signing command '{name}' is not available.");
        }
    }

    private PSObject? InvokeSingle(string commandName, Action<PowerShell> configure)
    {
        using var ps = CreatePowerShell();
        ps.AddCommand(commandName);
        configure(ps);

        var result = ps.Invoke();
        if (ps.HadErrors)
        {
            var error = ps.Streams.Error.FirstOrDefault();
            throw error?.Exception ?? new InvalidOperationException($"PowerShell command '{commandName}' failed.");
        }

        return result.FirstOrDefault();
    }

    private static PowerShell CreatePowerShell()
    {
        if (Runspace.DefaultRunspace is not null)
            return PowerShell.Create(RunspaceMode.CurrentRunspace);

        return PowerShell.Create();
    }

    private static bool IsInternalsPath(string filePath)
    {
        return filePath.IndexOf($"{Path.DirectorySeparatorChar}Internals{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               filePath.IndexOf("/Internals/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               filePath.IndexOf("\\Internals\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsCodeSigningCert(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey)
            return false;

        foreach (var ext in cert.Extensions)
        {
            if (ext is not X509EnhancedKeyUsageExtension eku)
                continue;

            foreach (var oid in eku.EnhancedKeyUsages)
            {
                if (string.Equals(oid.Value, "1.3.6.1.5.5.7.3.3", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static bool IsWindows()
    {
#if NET472
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
    }
}
