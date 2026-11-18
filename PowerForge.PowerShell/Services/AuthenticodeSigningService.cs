using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.ExceptionServices;
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
        catch (Exception ex)
        {
            // Keep the real cause: an access or provider failure is not the same as an empty store.
            _logger.Warn($"Could not read code-signing certificates from the {storeLocation} certificate store: {ex.Message}");
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
    /// Signs and timestamps one file with the certificate's private key, in the current process.
    /// Used to unlock a hardware token before a long run so its PIN prompt comes first.
    /// </summary>
    /// <param name="certificate">Signing certificate with a private key.</param>
    /// <param name="filePath">File to sign; existing signatures are replaced.</param>
    /// <param name="timeStampServer">RFC3161 timestamp server URL.</param>
    public void SignProofFile(X509Certificate2 certificate, string filePath, string timeStampServer)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", nameof(filePath));
        var signatures = SignFiles(new AuthenticodeSignRequest
        {
            Certificate = certificate,
            FilePaths = new[] { filePath },
            TimeStampServer = timeStampServer,
            HashAlgorithm = "SHA256",
            OverwriteSigned = true,
            MaxDegreeOfParallelism = 1
        });
        if (signatures.Count != 1)
            throw new InvalidOperationException($"Signing '{filePath}' did not return a signature.");
    }

    /// <summary>
    /// Signs a throwaway file so the signing key is unlocked now, in this process. A hardware
    /// token asks for its PIN here (once) instead of when release signing starts later.
    /// </summary>
    /// <param name="certificate">Signing certificate with a private key.</param>
    /// <param name="timeStampServer">RFC3161 timestamp server URL.</param>
    /// <returns>How long the unlock took, including any time spent at the PIN prompt.</returns>
    public TimeSpan UnlockSigningKey(X509Certificate2 certificate, string timeStampServer)
    {
        var watch = Stopwatch.StartNew();
        var directory = Path.Combine(Path.GetTempPath(), "PowerForge", "SigningUnlock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var proof = Path.Combine(directory, "signing-unlock.ps1");
            File.WriteAllText(proof, "# Signing key unlock proof; discarded after signing.\r\n$null = '" + Guid.NewGuid().ToString("D") + "'\r\n", new System.Text.UTF8Encoding(true));
            SignProofFile(certificate, proof, timeStampServer);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
        return watch.Elapsed;
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
            : GetFilesRequiringSignature("Get-AuthenticodeSignature", files, request);
        if (filesToSign.Length == 0)
            return Array.Empty<PSObject>();

        return SignScheduled(filesToSign, request, new[] { "Set-AuthenticodeSignature" }, (createPowerShell, file) =>
        {
            var result = InvokeMany("Set-AuthenticodeSignature", ps =>
            {
                ps.AddParameter("FilePath", file);
                ps.AddParameter("Certificate", request.Certificate);
                ps.AddParameter("TimestampServer", request.TimeStampServer);
                ps.AddParameter("IncludeChain", request.WindowsIncludeChain);
                ps.AddParameter("HashAlgorithm", request.HashAlgorithm);
                if (request.OverwriteSigned) ps.AddParameter("Force");
            }, createPowerShell);
            if (result.Count != 1)
                throw new InvalidOperationException($"Authenticode signing did not return exactly one signature for '{file}'.");
            var signature = result[0];
            var status = signature.Properties["Status"]?.Value?.ToString();
            if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Authenticode signing failed for '{file}': {status}. {signature.Properties["StatusMessage"]?.Value}");
            return signature;
        });
    }

    private IReadOnlyList<PSObject> SignWithOpenAuthenticode(string[] files, AuthenticodeSignRequest request)
    {
        return SignScheduled(files, request, new[] { "Set-OpenAuthenticodeSignature", "Get-OpenAuthenticodeSignature" }, (createPowerShell, file) =>
        {
            if (!request.OverwriteSigned && GetOpenSignatures(file, allowUnsigned: true, createPowerShell).Count > 0)
                return null;

            // The writer normally has no output; only a separate read validates the file.
            InvokeMany("Set-OpenAuthenticodeSignature", ps =>
            {
                ps.AddParameter("LiteralPath", file);
                ps.AddParameter("Certificate", request.Certificate);
                ps.AddParameter("TimeStampServer", request.TimeStampServer);
                ps.AddParameter("IncludeOption", request.NonWindowsIncludeChain);
                ps.AddParameter("HashAlgorithm", request.HashAlgorithm);
            }, createPowerShell);
            var result = GetOpenSignatures(file, allowUnsigned: false, createPowerShell);
            if (result.Count != 1)
                throw new InvalidOperationException($"Authenticode signing did not return exactly one signature for '{file}'.");
            var certificate = (X509Certificate2)result[0].Properties["Certificate"].Value;
            if (NormalizeThumbprint(certificate.Thumbprint) != NormalizeThumbprint(request.Certificate.Thumbprint))
                throw new InvalidOperationException($"Authenticode signing returned an unexpected signer for '{file}'.");
            return result[0];
        });
    }

    /// <summary>
    /// Signs the first file in the current runspace so any PIN or consent prompt happens once,
    /// then signs the rest concurrently in a runspace pool. Signs serially when the request asks
    /// for it or when the signing commands cannot be loaded into separate runspaces.
    /// </summary>
    private IReadOnlyList<PSObject> SignScheduled(
        string[] files,
        AuthenticodeSignRequest request,
        string[] signingCommands,
        Func<Func<PowerShell>, string, PSObject?> signOne)
    {
        var results = new PSObject?[files.Length];
        var progress = new SigningProgress(_logger, files.Length);
        var parallelism = files.Length > 1 ? SigningScheduler.ResolveParallelism(request.MaxDegreeOfParallelism) : 1;
        var pool = parallelism > 1 ? TryCreateSigningRunspacePool(signingCommands, Math.Min(parallelism, files.Length - 1)) : null;
        if (pool is null)
            parallelism = 1;
        else
            _logger.Verbose($"Signing the first file alone, then up to {parallelism} file(s) at a time.");

        try
        {
            Func<PowerShell> createPooled = () =>
            {
                var ps = PowerShell.Create();
                ps.RunspacePool = pool;
                return ps;
            };
            var outcome = SigningScheduler.Run(
                files.Length,
                parallelism,
                index =>
                {
                    results[index] = signOne(index == 0 || pool is null ? CreatePowerShell : createPooled, files[index]);
                    return true;
                },
                started: index => progress.Next(files[index]));

            var failures = outcome.Failures;
            if (failures.Count > 0)
            {
                foreach (var later in failures.Skip(1))
                    _logger.Warn($"Signing also failed for '{files[later.Index]}': {later.Error?.Message}");
                var first = failures[0];
                ExceptionDispatchInfo.Capture(
                    first.Error ?? new InvalidOperationException($"Authenticode signing failed for '{files[first.Index]}'.")).Throw();
            }
        }
        finally
        {
            pool?.Dispose();
        }

        var signatures = results.Where(signature => signature is not null).Select(signature => signature!).ToArray();
        progress.Complete(signatures.Length);
        return signatures;
    }

    /// <summary>
    /// Opens a runspace pool that loads the modules providing the signing commands. Returns null
    /// when a command is not a module command (for example a function defined in the session)
    /// or the pool cannot run it, so the caller signs serially in the current runspace.
    /// </summary>
    private RunspacePool? TryCreateSigningRunspacePool(string[] commandNames, int size)
    {
        RunspacePool? pool = null;
        try
        {
            var state = InitialSessionState.CreateDefault2();
#if !NET472
            // Worker runspaces load modules under the same policy as the calling runspace.
            // Windows PowerShell's reference assemblies do not expose this; there the
            // process-level policy applies to every runspace.
            if (Runspace.DefaultRunspace?.InitialSessionState is { } callerState)
                state.ExecutionPolicy = callerState.ExecutionPolicy;
#endif
            foreach (var name in commandNames)
            {
                using var ps = CreatePowerShell();
                ps.AddCommand("Get-Command").AddParameter("Name", name).AddParameter("ErrorAction", ActionPreference.SilentlyContinue);
                var command = ps.Invoke().Select(item => item.BaseObject).OfType<CommandInfo>().FirstOrDefault();
                var module = command?.Module;
                if (command is null || module is null ||
                    (command.CommandType != CommandTypes.Cmdlet && command.CommandType != CommandTypes.Function))
                {
                    _logger.Verbose($"Signing serially: '{name}' is not provided by a module that worker runspaces can load.");
                    return null;
                }

                state.ImportPSModule(new[] { !string.IsNullOrWhiteSpace(module.Path) && File.Exists(module.Path) ? module.Path : module.Name });
            }

            pool = RunspaceFactory.CreateRunspacePool(state);
            pool.SetMaxRunspaces(size);
            pool.SetMinRunspaces(1);
            pool.Open();
            foreach (var name in commandNames)
            {
                using var probe = PowerShell.Create();
                probe.RunspacePool = pool;
                probe.AddCommand("Get-Command").AddParameter("Name", name).AddParameter("ErrorAction", ActionPreference.SilentlyContinue);
                if (probe.Invoke().Count == 0)
                {
                    _logger.Verbose($"Signing serially: '{name}' is not available in worker runspaces.");
                    pool.Dispose();
                    return null;
                }
            }

            return pool;
        }
        catch (Exception ex)
        {
            _logger.Verbose($"Signing serially: worker runspaces could not be prepared ({ex.Message}).");
            pool?.Dispose();
            return null;
        }
    }

    private IReadOnlyList<PSObject> GetOpenSignatures(string file, bool allowUnsigned, Func<PowerShell> createPowerShell)
    {
        using var ps = createPowerShell();
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

    private string[] GetFilesRequiringSignature(string commandName, string[] files, AuthenticodeSignRequest request)
    {
        var signatures = InvokeMany(commandName, ps => ps.AddParameter("FilePath", files));
        var unsigned = new List<string>();

        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            var status = signature.Properties["Status"]?.Value?.ToString();
            var signer = signature.Properties["SignerCertificate"]?.Value as X509Certificate2;
            // A failed timestamp request can leave a valid code signature behind.
            // Repair only this identity's partial output; preserve vendor signatures.
            var needsTimestamp = !string.IsNullOrWhiteSpace(request.TimeStampServer) &&
                string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase) &&
                signer is not null &&
                NormalizeThumbprint(signer.Thumbprint) == NormalizeThumbprint(request.Certificate.Thumbprint) &&
                signature.Properties["TimeStamperCertificate"]?.Value is null;
            if (!string.Equals(status, "NotSigned", StringComparison.OrdinalIgnoreCase) && !needsTimestamp)
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

    private IReadOnlyList<PSObject> InvokeMany(string commandName, Action<PowerShell> configure, Func<PowerShell>? createPowerShell = null)
    {
        using var ps = (createPowerShell ?? CreatePowerShell)();
        ps.AddCommand(commandName);
        configure(ps);
        var result = ps.Invoke();
        foreach (var warning in ps.Streams.Warning)
            _logger.Warn($"{commandName}: {warning.Message}");
        if (ps.HadErrors)
        {
            var error = ps.Streams.Error.FirstOrDefault();
            throw error?.Exception ?? new InvalidOperationException($"PowerShell command '{commandName}' failed.");
        }

        return result.ToArray();
    }

    /// <summary>
    /// Reports signing progress at Info. Small batches show every file; large batches (a module can carry
    /// thousands of scripts) show a line every <see cref="ReportEveryFiles"/> files or <see cref="ReportInterval"/>.
    /// </summary>
    internal sealed class SigningProgress
    {
        internal const int ReportEveryFiles = 25;
        internal static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);

        private readonly ILogger _logger;
        private readonly int _total;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private TimeSpan _lastReport;
        private int _position;

        internal SigningProgress(ILogger logger, int total)
        {
            _logger = logger;
            _total = total;
        }

        internal void Next(string file)
        {
            _position++;
            var elapsed = _stopwatch.Elapsed;
            var everyFile = _total <= ReportEveryFiles;
            if (everyFile || _position == 1 || _position % ReportEveryFiles == 0 || elapsed - _lastReport >= ReportInterval)
            {
                _lastReport = elapsed;
                _logger.Info($"Signing {_position}/{_total}: {(everyFile ? Path.GetFileName(file) : file)}");
            }
            else
            {
                _logger.Verbose($"Signing {_position}/{_total}: {file}");
            }
        }

        internal void Complete(int signed)
        {
            _stopwatch.Stop();
            var skipped = _total - signed;
            _logger.Info(
                $"Signed {signed} of {_total} file(s) in {DotNetRepositoryReleaseService.FormatDuration(_stopwatch.Elapsed)}" +
                (skipped > 0 ? $" ({skipped} already signed)." : "."));
        }
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
