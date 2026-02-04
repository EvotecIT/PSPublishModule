using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Options for PowerShellGet <c>Find-Module</c>.
/// </summary>
public sealed class PowerShellGetFindOptions
{
    /// <summary>Module names to search for.</summary>
    public IReadOnlyList<string> Names { get; }
    /// <summary>Repository names to search in.</summary>
    public IReadOnlyList<string> Repositories { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PowerShellGetFindOptions(IReadOnlyList<string> names, bool prerelease = false, IReadOnlyList<string>? repositories = null, RepositoryCredential? credential = null)
    {
        Names = names ?? Array.Empty<string>();
        Prerelease = prerelease;
        Repositories = repositories ?? Array.Empty<string>();
        Credential = credential;
    }
}

/// <summary>
/// Options for PowerShellGet <c>Publish-Module</c>.
/// </summary>
public sealed class PowerShellGetPublishOptions
{
    /// <summary>Module path passed to <c>-Path</c>.</summary>
    public string Path { get; }
    /// <summary>Repository name passed to <c>-Repository</c>.</summary>
    public string? Repository { get; }
    /// <summary>NuGet API key passed to <c>-NuGetApiKey</c> when provided.</summary>
    public string? ApiKey { get; }
    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PowerShellGetPublishOptions(string path, string? repository = null, string? apiKey = null, RepositoryCredential? credential = null)
    {
        Path = path;
        Repository = repository;
        ApiKey = apiKey;
        Credential = credential;
    }
}

/// <summary>
/// Options for PowerShellGet <c>Save-Module</c>.
/// </summary>
public sealed class PowerShellGetSaveOptions
{
    /// <summary>Module name passed to <c>-Name</c>.</summary>
    public string Name { get; }

    /// <summary>Destination path passed to <c>-Path</c>.</summary>
    public string DestinationPath { get; }

    /// <summary>MinimumVersion passed to <c>-MinimumVersion</c> (optional).</summary>
    public string? MinimumVersion { get; }

    /// <summary>RequiredVersion passed to <c>-RequiredVersion</c> (optional).</summary>
    public string? RequiredVersion { get; }

    /// <summary>Repository name passed to <c>-Repository</c> (optional).</summary>
    public string? Repository { get; }

    /// <summary>Whether to allow prerelease versions when supported.</summary>
    public bool Prerelease { get; }

    /// <summary>Whether to accept license when supported.</summary>
    public bool AcceptLicense { get; }

    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PowerShellGetSaveOptions(
        string name,
        string destinationPath,
        string? minimumVersion = null,
        string? requiredVersion = null,
        string? repository = null,
        bool prerelease = false,
        bool acceptLicense = true,
        RepositoryCredential? credential = null)
    {
        Name = name;
        DestinationPath = destinationPath;
        MinimumVersion = minimumVersion;
        RequiredVersion = requiredVersion;
        Repository = repository;
        Prerelease = prerelease;
        AcceptLicense = acceptLicense;
        Credential = credential;
    }
}

/// <summary>
/// Out-of-process wrapper for PowerShellGet (Find-Module / Publish-Module / Register-PSRepository).
/// </summary>
public sealed class PowerShellGetClient
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new client using the provided runner and logger.
    /// </summary>
    public PowerShellGetClient(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds PowerShell modules using PowerShellGet.
    /// </summary>
    public IReadOnlyList<PSResourceInfo> Find(PowerShellGetFindOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var names = (options.Names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0) throw new ArgumentException("At least one name is required.", nameof(options));

        var repos = (options.Repositories ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var script = BuildFindScript();
        var args = new List<string>(5)
        {
            EncodeLines(names),
            EncodeLines(repos),
            options.Prerelease ? "1" : "0",
            options.Credential?.UserName ?? string.Empty,
            options.Credential?.Secret ?? string.Empty
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(2));
        var items = ParseFindOutput(result.StdOut);

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Find-Module failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }

        return items;
    }

    /// <summary>
    /// Publishes a PowerShell module using PowerShellGet.
    /// </summary>
    public void Publish(PowerShellGetPublishOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Path)) throw new ArgumentException("Path is required.", nameof(options));

        var script = BuildPublishScript();
        var args = new List<string>(4)
        {
            options.Path,
            options.Repository ?? string.Empty,
            options.ApiKey ?? string.Empty,
            options.Credential?.UserName ?? string.Empty,
            options.Credential?.Secret ?? string.Empty
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Publish-Module failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }
    }

    /// <summary>
    /// Saves a PowerShell module using PowerShellGet (<c>Save-Module</c>).
    /// </summary>
    public IReadOnlyList<PSResourceInfo> Save(PowerShellGetSaveOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DestinationPath)) throw new ArgumentException("DestinationPath is required.", nameof(options));

        var dest = Path.GetFullPath(options.DestinationPath);
        Directory.CreateDirectory(dest);

        var script = BuildSaveScript();
        var args = new List<string>(9)
        {
            options.Name,
            options.MinimumVersion ?? string.Empty,
            options.RequiredVersion ?? string.Empty,
            options.Repository ?? string.Empty,
            dest,
            options.Prerelease ? "1" : "0",
            options.AcceptLicense ? "1" : "0",
            options.Credential?.UserName ?? string.Empty,
            options.Credential?.Secret ?? string.Empty
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        var items = ParseSaveOutput(result.StdOut);

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Save-Module failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }

        return items;
    }

    /// <summary>
    /// Ensures the given repository is registered with PowerShellGet and returns true when it was created by this call.
    /// </summary>
    public bool EnsureRepositoryRegistered(string name, string sourceUri, string publishUri, bool trusted = true, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(sourceUri)) throw new ArgumentException("SourceUri is required.", nameof(sourceUri));
        if (string.IsNullOrWhiteSpace(publishUri)) throw new ArgumentException("PublishUri is required.", nameof(publishUri));

        var script = BuildEnsureRepositoryScript();
        var args = new List<string>(4)
        {
            name.Trim(),
            sourceUri.Trim(),
            publishUri.Trim(),
            trusted ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(2));
        var created = ParseRepositoryCreated(result.StdOut);

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Register-PSRepository failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }

        return created;
    }

    /// <summary>
    /// Unregisters a PowerShellGet repository by name.
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
            var full = $"Unregister-PSRepository failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "powershellget");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"powershellget_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines ?? Array.Empty<string>());
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string? TryExtractError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPWSGET::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFPWSGET::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
    }

    private static bool ParseRepositoryCreated(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPWSGET::REPO::CREATED::", StringComparison.Ordinal)) continue;
            var flag = line.Substring("PFPWSGET::REPO::CREATED::".Length);
            return string.Equals(flag, "1", StringComparison.Ordinal);
        }
        return false;
    }

    private static IReadOnlyList<PSResourceInfo> ParseFindOutput(string stdout)
    {
        var list = new List<PSResourceInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPWSGET::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 5) continue;

            var name = Decode(parts[2]);
            var version = Decode(parts[3]);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;

            var repo = Decode(parts[4]);
            var guid = parts.Length > 5 ? Decode(parts[5]) : null;
            if (string.IsNullOrWhiteSpace(guid)) guid = null;
            list.Add(new PSResourceInfo(name, version, string.IsNullOrWhiteSpace(repo) ? null : repo, author: null, description: null, guid: guid));
        }
        return list;
    }

    private static IReadOnlyList<PSResourceInfo> ParseSaveOutput(string stdout)
    {
        var list = new List<PSResourceInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPWSGET::SAVE::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 5) continue;

            var name = Decode(parts[3]);
            var version = Decode(parts[4]);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
            list.Add(new PSResourceInfo(name, version, repository: null, author: null, description: null));
        }
        return list;
    }

    private static string BuildFindScript()
    {
        return EmbeddedScripts.Load("Scripts/PowerShellGet/Find-Module.ps1");
}

    private static string BuildPublishScript()
    {
        return EmbeddedScripts.Load("Scripts/PowerShellGet/Publish-Module.ps1");
}

    private static string BuildSaveScript()
    {
        return EmbeddedScripts.Load("Scripts/PowerShellGet/Save-Module.ps1");
}

    private static string BuildEnsureRepositoryScript()
    {
        return EmbeddedScripts.Load("Scripts/PowerShellGet/Ensure-Repository.ps1");
}

    private static string BuildUnregisterRepositoryScript()
    {
        return EmbeddedScripts.Load("Scripts/PowerShellGet/Unregister-Repository.ps1");
}
}

