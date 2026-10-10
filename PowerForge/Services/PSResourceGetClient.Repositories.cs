using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Repository management helpers for PSResourceGet (out-of-process).
/// </summary>
public sealed partial class PSResourceGetClient
{
    /// <summary>
    /// Ensures the given repository is registered with PSResourceGet and returns true when it was created by this call.
    /// </summary>
    public bool EnsureRepositoryRegistered(
        string name,
        string uri,
        bool trusted = true,
        int? priority = null,
        RepositoryApiVersion apiVersion = RepositoryApiVersion.Auto,
        TimeSpan? timeout = null)
        => ParseRepositoryCreated(EnsureRepositoryRegistration(name, uri, trusted, priority, apiVersion, timeout, temporary: false).StdOut);

    /// <summary>Reuses an existing upload URI registration or creates a temporary alias without changing other registrations.</summary>
    internal (string Name, bool Created) AcquirePublishRepository(
        string uri,
        bool trusted,
        int? priority,
        RepositoryApiVersion apiVersion)
    {
        var name = "PowerForgePublish-" + Guid.NewGuid().ToString("N");
        var result = EnsureRepositoryRegistration(name, uri, trusted, priority, apiVersion, null, temporary: true);
        foreach (var line in SplitLines(result.StdOut))
        {
            if (line.StartsWith("PFPSRG::REPO::NAME::", StringComparison.Ordinal))
                return (Decode(line.Substring("PFPSRG::REPO::NAME::".Length)), ParseRepositoryCreated(result.StdOut));
        }
        throw new InvalidOperationException("PSResourceGet did not report the acquired publish repository name.");
    }

    private PowerShellRunResult EnsureRepositoryRegistration(
        string name, string uri, bool trusted, int? priority,
        RepositoryApiVersion apiVersion, TimeSpan? timeout, bool temporary)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("Uri is required.", nameof(uri));

        var script = BuildEnsureRepositoryScript();
        var args = new List<string>(6)
        {
            name.Trim(),
            uri.Trim(),
            trusted ? "1" : "0",
            priority.HasValue ? priority.Value.ToString() : string.Empty,
            apiVersion == RepositoryApiVersion.Auto ? string.Empty : apiVersion.ToString(),
            temporary ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(2));

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Register-PSResourceRepository failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }

        return result;
    }

    /// <summary>
    /// Ensures Microsoft Artifact Registry is registered with PSResourceGet.
    /// </summary>
    public bool EnsureMicrosoftArtifactRegistryRegistered(
        string? name = null,
        bool trusted = true,
        int? priority = null,
        TimeSpan? timeout = null)
    {
        return EnsureRepositoryRegistered(
            string.IsNullOrWhiteSpace(name) ? MicrosoftArtifactRegistryRepository.DefaultName : name!.Trim(),
            MicrosoftArtifactRegistryRepository.DefaultUri,
            trusted,
            priority,
            RepositoryApiVersion.ContainerRegistry,
            timeout);
    }

    /// <summary>
    /// Unregisters a PSResourceGet repository by name.
    /// </summary>
    public void UnregisterRepository(string name, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        var script = BuildUnregisterRepositoryScript();
        var args = new List<string>(1) { name.Trim() };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Unregister-PSResourceRepository failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }
    }

    private static bool ParseRepositoryCreated(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::REPO::CREATED::", StringComparison.Ordinal)) continue;
            var flag = line.Substring("PFPSRG::REPO::CREATED::".Length);
            return string.Equals(flag, "1", StringComparison.Ordinal);
        }
        return false;
    }

    private static string BuildEnsureRepositoryScript()
    {
        return EmbeddedScripts.Load("Scripts/PSResourceGet/Ensure-Repository.ps1");
}

    private static string BuildUnregisterRepositoryScript()
    {
        return EmbeddedScripts.Load("Scripts/PSResourceGet/Unregister-Repository.ps1");
}
}
