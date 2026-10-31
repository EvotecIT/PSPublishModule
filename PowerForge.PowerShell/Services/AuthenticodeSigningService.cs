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
                    Certificate = SelectValidCertificate(certs.FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == normalized)),
                    AvailableCertificates = certs
                };
            }

            certs = certs.Where(c => c.NotBefore.ToUniversalTime() <= DateTime.UtcNow &&
                                     c.NotAfter.ToUniversalTime() > DateTime.UtcNow).ToArray();
            return new CodeSigningCertificateLookupResult
            {
                Certificate = certs.Length == 1 ? certs[0] : null,
                AvailableCertificates = certs
            };
        }
        catch (InvalidOperationException)
        {
            throw;
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
        CodeSigningCertificateValidation.Validate(request.Certificate);

        var files = (request.FilePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return Array.Empty<PSObject>();

        EnsureSigningCommandsAvailable();
        return IsWindows()
            ? SignWithWindowsAuthenticode(files, request)
            : SignWithOpenAuthenticode(files, request);
    }

    /// <summary>
    /// Ensures that the platform-specific Authenticode signing commands are available.
    /// </summary>
    public void EnsureSigningCommandsAvailable()
    {
        if (IsWindows())
        {
            EnsureCommandAvailable("Get-AuthenticodeSignature");
            EnsureCommandAvailable("Set-AuthenticodeSignature");
            return;
        }

        EnsureCommandAvailable("Get-OpenAuthenticodeSignature");
        EnsureCommandAvailable("Set-OpenAuthenticodeSignature");
    }

    private IReadOnlyList<PSObject> SignWithWindowsAuthenticode(string[] files, AuthenticodeSignRequest request)
    {
        var filesToSign = request.OverwriteSigned
            ? files
            : GetUnsignedFiles("Get-AuthenticodeSignature", files);
        if (filesToSign.Length == 0)
            return Array.Empty<PSObject>();

        return SignIndividually("Set-AuthenticodeSignature", filesToSign, (ps, file) =>
        {
            ps.AddParameter("FilePath", file);
            ps.AddParameter("Certificate", request.Certificate);
            ps.AddParameter("TimestampServer", request.TimeStampServer);
            ps.AddParameter("IncludeChain", request.WindowsIncludeChain);
            ps.AddParameter("HashAlgorithm", request.HashAlgorithm);
            if (request.OverwriteSigned) ps.AddParameter("Force");
        });
    }

    private IReadOnlyList<PSObject> SignWithOpenAuthenticode(string[] files, AuthenticodeSignRequest request)
    {
        var signatures = new List<PSObject>(files.Length);
        foreach (var file in files)
        {
            if (!request.OverwriteSigned && GetOpenSignatures(file, allowUnsigned: true).Count > 0)
                continue;

            _logger.Verbose($"Signing file: {file}");
            // The writer normally has no output; only a separate read validates the file.
            InvokeMany("Set-OpenAuthenticodeSignature", ps =>
            {
                ps.AddParameter("LiteralPath", file);
                ps.AddParameter("Certificate", request.Certificate);
                ps.AddParameter("TimeStampServer", request.TimeStampServer);
                ps.AddParameter("IncludeOption", request.NonWindowsIncludeChain);
                ps.AddParameter("HashAlgorithm", request.HashAlgorithm);
            });
            var result = GetOpenSignatures(file, allowUnsigned: false);
            if (result.Count != 1)
                throw new InvalidOperationException($"Authenticode signing did not return exactly one signature for '{file}'.");
            var certificate = (X509Certificate2)result[0].Properties["Certificate"].Value;
            if (NormalizeThumbprint(certificate.Thumbprint) != NormalizeThumbprint(request.Certificate.Thumbprint))
                throw new InvalidOperationException($"Authenticode signing returned an unexpected signer for '{file}'.");
            signatures.Add(result[0]);
        }
        return signatures;
    }

    private IReadOnlyList<PSObject> GetOpenSignatures(string file, bool allowUnsigned)
    {
        using var ps = CreatePowerShell();
        ps.AddCommand("Get-OpenAuthenticodeSignature")
            .AddParameter("LiteralPath", file)
            .AddParameter("ErrorAction", ActionPreference.Continue);
        var result = ps.Invoke();
        if (ps.HadErrors)
        {
            // NoSignature is the provider's unsigned-file contract. Validation/read
            // failures must never turn into permission to replace an existing signature.
            if (allowUnsigned && result.Count == 0 && ps.Streams.Error.Count > 0 &&
                ps.Streams.Error.All(error => string.Equals(error.FullyQualifiedErrorId.Split(',')[0], "NoSignature", StringComparison.Ordinal)))
                return Array.Empty<PSObject>();
            throw new InvalidOperationException($"Authenticode signature validation failed for '{file}'.", ps.Streams.Error.FirstOrDefault()?.Exception);
        }
        if (result.Count == 0 || result.Any(signature =>
                signature.Properties["Certificate"]?.Value is not X509Certificate2 certificate ||
                string.IsNullOrWhiteSpace(certificate.Thumbprint)))
            throw new InvalidOperationException($"Authenticode signature validation did not return a signer certificate for '{file}'.");
        return result.ToArray();
    }

    private IReadOnlyList<PSObject> SignIndividually(string command, string[] files, Action<PowerShell, string> configure)
    {
        var signatures = new List<PSObject>(files.Length);
        foreach (var file in files)
        {
            _logger.Verbose($"Signing file: {file}");
            var result = InvokeMany(command, ps => configure(ps, file));
            if (result.Count != 1)
                throw new InvalidOperationException($"Authenticode signing did not return exactly one signature for '{file}'.");
            var signature = result[0];
            var status = signature.Properties["Status"]?.Value?.ToString();
            if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Authenticode signing failed for '{file}': {status}. {signature.Properties["StatusMessage"]?.Value}");
            signatures.Add(signature);
        }
        return signatures;
    }

    private string[] GetUnsignedFiles(string commandName, string[] files)
    {
        var signatures = InvokeMany(commandName, ps => ps.AddParameter("FilePath", files));
        var unsigned = new List<string>();

        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            var status = signature.Properties["Status"]?.Value?.ToString();
            if (!string.Equals(status, "NotSigned", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = signature.Properties["Path"]?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(path))
                unsigned.Add(path!);
            else if (i < files.Length)
                unsigned.Add(files[i]);
        }

        return unsigned
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        var result = InvokeMany(commandName, configure);
        return result.FirstOrDefault();
    }

    private IReadOnlyList<PSObject> InvokeMany(string commandName, Action<PowerShell> configure)
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

        return result.ToArray();
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

    private static X509Certificate2? SelectValidCertificate(X509Certificate2? certificate)
    {
        if (certificate is not null)
            CodeSigningCertificateValidation.Validate(certificate);
        return certificate;
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
