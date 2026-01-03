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
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("Uri is required.", nameof(uri));

        var script = BuildEnsureRepositoryScript();
        var args = new List<string>(5)
        {
            name.Trim(),
            uri.Trim(),
            trusted ? "1" : "0",
            priority.HasValue ? priority.Value.ToString() : string.Empty,
            apiVersion == RepositoryApiVersion.Auto ? string.Empty : apiVersion.ToString()
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(2));
        var created = ParseRepositoryCreated(result.StdOut);

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

        return created;
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

