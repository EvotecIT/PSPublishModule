using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class PrivateModuleCommandSupport
{
    internal static IReadOnlyDictionary<string, string> CreateVersionMap(string[] names, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
                map[name.Trim()] = version!.Trim();
        }

        return map;
    }

    internal static string ResolveManagedRepositorySource(PSCmdlet cmdlet, string repository)
    {
        if (TryResolveManagedRepositorySource(cmdlet, repository, out var source))
            return source!;

        if (string.IsNullOrWhiteSpace(repository))
            throw new InvalidOperationException("Managed private module delivery requires a repository URL or local feed path.");

        throw new InvalidOperationException(
            $"Repository '{repository}' looks like a registered PowerShell repository name. Managed private module delivery needs a repository URL or existing local feed path; use the default PrivateModule transport for registered repository names.");
    }

    internal static bool TryResolveManagedRepositorySource(PSCmdlet cmdlet, string? repository, out string? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(repository))
            return false;

        var trimmed = repository!.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            source = trimmed;
            return true;
        }

        var providerPath = ManagedModuleCommandSupport.ResolveProviderPath(cmdlet, trimmed);
        if (!string.IsNullOrWhiteSpace(providerPath) && Directory.Exists(providerPath))
        {
            source = providerPath!;
            return true;
        }

        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            source = providerPath ?? trimmed;
            return true;
        }

        if (new PowerShellRepositorySourceResolver().TryResolveSource(cmdlet, trimmed, out var registeredSource))
        {
            source = registeredSource;
            return true;
        }

        return false;
    }
}
