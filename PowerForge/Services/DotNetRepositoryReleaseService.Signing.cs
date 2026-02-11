using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static bool SignPackages(
        IReadOnlyList<string> packages,
        DotNetRepositoryReleaseSpec spec,
        string sha256,
        out string error)
    {
        error = string.Empty;
        if (packages is null || packages.Count == 0) return true;

        var store = spec.CertificateStore == CertificateStoreLocation.LocalMachine ? "LocalMachine" : "CurrentUser";
        var timeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer!.Trim();

        foreach (var pkg in packages)
        {
            var exitCode = RunDotnetSign(pkg, sha256, store, timeStampServer, out var stdErr, out var stdOut);
            if (exitCode == 0) continue;

            var msg = string.Join(Environment.NewLine, stdErr, stdOut).Trim();
            error = $"Signing failed for {Path.GetFileName(pkg)}. {msg}".Trim();
            return false;
        }

        return true;
    }

    private static int RunDotnetSign(
        string packagePath,
        string sha256,
        string store,
        string timeStampServer,
        out string stdErr,
        out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string>
        {
            "nuget", "sign", packagePath,
            "--certificate-fingerprint", sha256,
            "--certificate-store-location", store,
            "--certificate-store-name", "My",
            "--timestamper", timeStampServer,
            "--overwrite"
        };
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("sign");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--certificate-fingerprint");
        psi.ArgumentList.Add(sha256);
        psi.ArgumentList.Add("--certificate-store-location");
        psi.ArgumentList.Add(store);
        psi.ArgumentList.Add("--certificate-store-name");
        psi.ArgumentList.Add("My");
        psi.ArgumentList.Add("--timestamper");
        psi.ArgumentList.Add(timeStampServer);
        psi.ArgumentList.Add("--overwrite");
#endif

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string? GetCertificateSha256(string thumbprint, CertificateStoreLocation storeLocation)
    {
        try
        {
            var loc = storeLocation == CertificateStoreLocation.LocalMachine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, loc);
            store.Open(OpenFlags.ReadOnly);
            var cert = store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == NormalizeThumbprint(thumbprint));
            if (cert is null) return null;
#if NET472
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(cert.RawData);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
#else
            return cert.GetCertHashString(HashAlgorithmName.SHA256);
#endif
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static bool MatchesExpectedMap(string projectName, Dictionary<string, string> expectedMap, bool allowWildcards)
    {
        foreach (var kvp in expectedMap)
        {
            if (MatchesPattern(projectName, kvp.Key, allowWildcards))
                return true;
        }

        return false;
    }

    private static bool MatchesPattern(string value, string pattern, bool allowWildcards)
    {
        if (!allowWildcards || string.IsNullOrWhiteSpace(pattern))
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        if (!ContainsWildcard(pattern))
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    private static bool ContainsWildcard(string value)
        => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

}
