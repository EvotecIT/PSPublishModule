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

/// <summary>
/// Repository-wide release workflow for .NET packages (discover, version, pack, publish).
/// </summary>
public sealed partial class DotNetRepositoryReleaseService
{
    internal delegate bool PackageSigningHandler(
        IReadOnlyList<string> packages,
        DotNetRepositoryReleaseSpec spec,
        string sha256,
        out string error);

    private readonly ILogger _logger;
    private readonly NuGetPackageVersionResolver _resolver;
    private readonly PackageSigningHandler _signPackages;
    private readonly Func<string, CertificateStoreLocation, string?> _getCertificateSha256;
    private static readonly string[] DefaultExcludeDirectories =
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "packages",
        "Artifacts", "Artefacts", "TestResults", "Ignore"
    };

    /// <summary>
    /// Creates a new repository release service.
    /// </summary>
    public DotNetRepositoryReleaseService(ILogger logger)
        : this(logger, SignPackages, GetCertificateSha256)
    {
    }

    internal DotNetRepositoryReleaseService(
        ILogger logger,
        PackageSigningHandler? signPackages,
        Func<string, CertificateStoreLocation, string?>? getCertificateSha256)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolver = new NuGetPackageVersionResolver(_logger);
        _signPackages = signPackages ?? SignPackages;
        _getCertificateSha256 = getCertificateSha256 ?? GetCertificateSha256;
    }

}
