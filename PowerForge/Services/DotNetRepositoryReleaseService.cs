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
    private readonly ILogger _logger;
    private readonly NuGetPackageVersionResolver _resolver;
    private static readonly string[] DefaultExcludeDirectories =
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "packages",
        "Artifacts", "Artefacts", "TestResults", "Ignore"
    };

    /// <summary>
    /// Creates a new repository release service.
    /// </summary>
    public DotNetRepositoryReleaseService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolver = new NuGetPackageVersionResolver(_logger);
    }

}
