using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateDesiredModule
{
    internal ModuleStateDesiredModule(
        string name,
        string? versionPolicy = null,
        IEnumerable<string>? allowedSources = null,
        string? scope = null,
        string? targetPath = null,
        string? expectedPackageSha256 = null,
        bool includePrerelease = false,
        bool force = false,
        bool acceptLicense = false,
        bool allowClobber = false,
        bool skipDependencyCheck = false,
        string? targetRepositorySource = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Desired module name is required.", nameof(name))
            : name.Trim();
        VersionPolicy = string.IsNullOrWhiteSpace(versionPolicy) ? "*" : versionPolicy!.Trim();
        AllowedSources = (allowedSources ?? Array.Empty<string>())
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Scope = string.IsNullOrWhiteSpace(scope) ? null : scope!.Trim();
        TargetPath = string.IsNullOrWhiteSpace(targetPath) ? null : targetPath!.Trim();
        ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(expectedPackageSha256);
        IncludePrerelease = includePrerelease;
        Force = force;
        AcceptLicense = acceptLicense;
        AllowClobber = allowClobber;
        SkipDependencyCheck = skipDependencyCheck;
        TargetRepositorySource = string.IsNullOrWhiteSpace(targetRepositorySource) ? null : targetRepositorySource!.Trim();
    }

    internal string Name { get; }

    internal string VersionPolicy { get; }

    internal string[] AllowedSources { get; }

    internal string? Scope { get; }

    internal string? TargetPath { get; }

    internal string? ExpectedPackageSha256 { get; }

    internal bool IncludePrerelease { get; }

    internal bool Force { get; }

    internal bool AcceptLicense { get; }

    internal bool AllowClobber { get; }

    internal bool SkipDependencyCheck { get; }

    internal string? TargetRepositorySource { get; }
}
