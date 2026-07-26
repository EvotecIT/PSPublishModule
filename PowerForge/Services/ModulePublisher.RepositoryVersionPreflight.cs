using System;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePublisher
{
    private RepositoryVersionState GetRepositoryVersionState(
        PublishTool tool,
        string moduleName,
        string repositoryName,
        RepositoryCredential? credential,
        SemVer publishVersion,
        string publishVersionText,
        bool checkExactVersion)
    {
        SemVer? latest = null;
        var exactVersionExists = false;
        if (checkExactVersion)
        {
            try
            {
                exactVersionExists = RepositoryVersionExists(
                    tool,
                    moduleName,
                    publishVersionText,
                    repositoryName,
                    credential,
                    publishVersion);
            }
            catch (Exception ex) when (IsRepositoryPackageNotFound(moduleName, ex))
            {
                exactVersionExists = false;
            }

            if (exactVersionExists)
                return new RepositoryVersionState(publishVersion, exactVersionExists: true);
        }

        if (string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var version in _powerShellGalleryFeed.GetVersions(moduleName, includePrerelease: true, timeout: TimeSpan.FromMinutes(2)))
                {
                    if (!TryParseSemVer(version.VersionText, out var parsed))
                        continue;

                    if (parsed.CompareTo(publishVersion) == 0)
                        exactVersionExists = true;
                    if (latest is null || parsed.CompareTo(latest.Value) > 0)
                        latest = parsed;
                }

                if (latest is not null)
                    return new RepositoryVersionState(latest, exactVersionExists);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to query the raw PowerShell Gallery feed for '{moduleName}'. Falling back to {tool}. {ex.Message}");
            }
        }

        var versions = tool == PublishTool.PowerShellGet
            ? _powerShellGet.Find(
                    new PowerShellGetFindOptions(
                        names: new[] { moduleName },
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(GetRepositoryVersionText)
            : _psResourceGet.Find(
                    new PSResourceFindOptions(
                        names: new[] { moduleName },
                        version: null,
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(GetRepositoryVersionText);

        foreach (var version in versions)
        {
            if (!TryParseSemVer(version, out var parsed))
                continue;

            if (parsed.CompareTo(publishVersion) == 0)
                exactVersionExists = true;
            if (latest is null || parsed.CompareTo(latest.Value) > 0)
                latest = parsed;
        }

        return new RepositoryVersionState(latest, exactVersionExists);
    }

    private bool RepositoryVersionExists(
        PublishTool tool,
        string moduleName,
        string publishVersionText,
        string repositoryName,
        RepositoryCredential? credential,
        SemVer publishVersion)
    {
        if (string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            return _powerShellGalleryFeed.VersionExists(
                moduleName,
                publishVersionText,
                timeout: TimeSpan.FromMinutes(2));
        }

        var versions = tool == PublishTool.PowerShellGet
            ? _powerShellGet.Find(
                    new PowerShellGetFindOptions(
                        names: new[] { moduleName },
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(GetRepositoryVersionText)
            : _psResourceGet.Find(
                    new PSResourceFindOptions(
                        names: new[] { moduleName },
                        version: publishVersionText,
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(GetRepositoryVersionText);

        foreach (var version in versions)
        {
            if (TryParseSemVer(version, out var parsed) && parsed.CompareTo(publishVersion) == 0)
                return true;
        }

        return false;
    }

    private readonly struct RepositoryVersionState
    {
        internal RepositoryVersionState(SemVer? latest, bool exactVersionExists)
        {
            Latest = latest;
            ExactVersionExists = exactVersionExists;
        }

        internal SemVer? Latest { get; }

        internal bool ExactVersionExists { get; }
    }
}
