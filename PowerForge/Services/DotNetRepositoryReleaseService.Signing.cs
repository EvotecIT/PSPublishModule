using System;
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
        out string[] failedPackages,
        out string error)
    {
        failedPackages = Array.Empty<string>();
        error = string.Empty;
        if (packages is null || packages.Count == 0) return true;

        var store = spec.CertificateStore == CertificateStoreLocation.LocalMachine ? "LocalMachine" : "CurrentUser";
        var timeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer!.Trim();

        var packagePaths = packages
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packagePaths.Length == 0) return true;

        DotNetNuGetSignResult result = RunDotnetSign(
            packagePaths,
            sha256,
            store,
            timeStampServer,
            spec.OverwriteSignedPackages,
            out failedPackages);
        if (result.Succeeded) return true;

        error = result.ErrorMessage
            ?? string.Join(Environment.NewLine, result.StdErr, result.StdOut).Trim();
        return false;
    }

    private static DotNetNuGetSignResult RunDotnetSign(
        IReadOnlyList<string> packagePaths,
        string sha256,
        string store,
        string timeStampServer,
        bool overwriteSignedPackages,
        out string[] failedPackages)
    {
        (DotNetNuGetSignResult Result, string[] FailedPackages) outcome = new DotNetNuGetClient()
            .SignPackagesIndividuallyAsync(new DotNetNuGetSignRequest(
                packagePaths: packagePaths,
                certificateFingerprint: sha256,
                certificateStoreLocation: store,
                timeStampServer: timeStampServer,
                overwrite: overwriteSignedPackages))
            .GetAwaiter()
            .GetResult();

        failedPackages = outcome.FailedPackages;
        return outcome.Result;
    }

    private static void MarkPackageSigningFailure(
        IEnumerable<DotNetRepositoryProjectResult> projects,
        IReadOnlyList<string> packagesToSign,
        string signError)
    {
        var failedPackages = new HashSet<string>(
            packagesToSign.Where(package => !string.IsNullOrWhiteSpace(package)),
            StringComparer.OrdinalIgnoreCase);

        if (failedPackages.Count == 0)
            return;

        var message = string.IsNullOrWhiteSpace(signError)
            ? "Package signing failed."
            : $"Package signing failed: {signError}";

        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                continue;

            if (project.Packages.Concat(project.SymbolPackages).Any(package => failedPackages.Contains(package)))
                project.ErrorMessage = message;
        }
    }

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
