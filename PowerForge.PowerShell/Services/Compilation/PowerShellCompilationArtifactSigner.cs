namespace PowerForge;

/// <summary>
/// Applies Authenticode signatures to staged compilation artifacts before integrity evidence is calculated.
/// </summary>
internal static class PowerShellCompilationArtifactSigner
{
    private static readonly HashSet<string> SignableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ps1", ".psm1", ".psd1"
    };

    private static readonly HashSet<string> BuildOwnedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Primary", "PrimaryModule", "TypedAssembly", "GeneratedAssembly", "PrimaryModuleManifest"
    };

    internal static PowerShellCompilationSigningResult? Sign(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<PowerShellCompilationArtifactFile> files)
    {
        if (!spec.SignArtifact)
            return null;
        if (!IsWindows())
            throw new PlatformNotSupportedException("Compilation artifact signing currently requires the Windows certificate store and Windows Authenticode provider.");

        var service = new AuthenticodeSigningService(new NullLogger());
        var lookup = service.SelectCertificateFromStore(spec.CertificateStoreLocation, spec.CertificateThumbprint);
        if (lookup.Certificate is null)
        {
            var requested = string.IsNullOrWhiteSpace(spec.CertificateThumbprint)
                ? "a unique code-signing certificate"
                : $"code-signing certificate '{spec.CertificateThumbprint}'";
            throw new InvalidOperationException($"Could not select {requested} from {spec.CertificateStoreLocation}\\My.");
        }

        var signableFiles = GetBuildOwnedSignableFiles(files);
        if (signableFiles.Length == 0)
            throw new InvalidOperationException("Signing was requested, but the generated artifact contains no Authenticode-signable files.");

        var certificateThumbprint = NormalizeThumbprint(lookup.Certificate.Thumbprint);
        var fileLiterals = string.Join(", ", signableFiles.Select(QuotePowerShellLiteral));
        var script = string.Join("; ", new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "Import-Module Microsoft.PowerShell.Security -ErrorAction Stop",
            $"$store = [System.Security.Cryptography.X509Certificates.X509Store]::new([System.Security.Cryptography.X509Certificates.StoreName]::My, [System.Security.Cryptography.X509Certificates.StoreLocation]::{spec.CertificateStoreLocation})",
            "$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)",
            $"$certificate = @($store.Certificates | Where-Object {{ ($_.Thumbprint -replace ' ', '').ToUpperInvariant() -eq {QuotePowerShellLiteral(certificateThumbprint)} }}) | Select-Object -First 1",
            "if (-not $certificate) { throw 'The selected code-signing certificate was not available in the isolated signing host.' }",
            $"$files = @({fileLiterals})",
            $"$signatures = @(Set-AuthenticodeSignature -FilePath $files -Certificate $certificate -TimestampServer {QuotePowerShellLiteral(spec.TimeStampServer)} -IncludeChain All -HashAlgorithm SHA256 -Force -ErrorAction Stop)",
            "$store.Dispose()",
            "$invalidStatuses = @($signatures | Where-Object Status -ne 'Valid' | ForEach-Object { $_.Status.ToString() } | Sort-Object -Unique)",
            "if ($signatures.Count -ne $files.Count -or $invalidStatuses.Count -ne 0) { throw ('Authenticode signing did not return a valid signature for every generated signable file. Statuses: ' + ($invalidStatuses -join ', ')) }"
        });
        var windowsPowerShellRoot = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0");
        var execution = new PowerShellRunner().Run(PowerShellRunRequest.ForCommand(
            script,
            TimeSpan.FromSeconds(spec.SigningTimeoutSeconds),
            preferPwsh: false,
            workingDirectory: Path.GetDirectoryName(signableFiles[0]),
            environmentVariables: new Dictionary<string, string?>
            {
                ["PSModulePath"] = Path.Combine(windowsPowerShellRoot, "Modules")
            },
            executableOverride: Path.Combine(windowsPowerShellRoot, "powershell.exe")));
        if (execution.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(execution.StdErr) ? execution.StdOut : execution.StdErr;
            throw new InvalidOperationException("Authenticode signing failed in the isolated PowerShell host. " + Bound(detail));
        }

        return new PowerShellCompilationSigningResult(
            NormalizeThumbprint(lookup.Certificate.Thumbprint),
            signableFiles.Length);
    }

    internal static string[] GetBuildOwnedSignableFiles(IEnumerable<PowerShellCompilationArtifactFile> files)
        => files
            .Where(static file => BuildOwnedRoles.Contains(file.Role))
            .Select(static file => file.Path)
            .Where(path => SignableExtensions.Contains(Path.GetExtension(path)))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static string QuotePowerShellLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    private static string Bound(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= 2048 ? normalized : normalized.Substring(normalized.Length - 2048);
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

internal sealed class PowerShellCompilationSigningResult
{
    internal PowerShellCompilationSigningResult(string certificateThumbprint, int signedFiles)
    {
        CertificateThumbprint = certificateThumbprint;
        SignedFiles = signedFiles;
    }

    internal string CertificateThumbprint { get; }
    internal int SignedFiles { get; }
}
