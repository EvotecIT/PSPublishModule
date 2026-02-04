using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// A minimal representation of a PowerShell resource returned by repository queries (PSResourceGet/PowerShellGet).
/// </summary>
public sealed class PSResourceInfo
{
    /// <summary>Name of the resource.</summary>
    public string Name { get; }
    /// <summary>Resolved resource version.</summary>
    public string Version { get; }
    /// <summary>Resource GUID (when available).</summary>
    public string? Guid { get; }
    /// <summary>Repository name (when available).</summary>
    public string? Repository { get; }
    /// <summary>Author (when available).</summary>
    public string? Author { get; }
    /// <summary>Description (when available).</summary>
    public string? Description { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PSResourceInfo(string name, string version, string? repository, string? author, string? description, string? guid = null)
    {
        Name = name;
        Version = version;
        Guid = guid;
        Repository = repository;
        Author = author;
        Description = description;
    }
}

/// <summary>
/// Options for <c>Find-PSResource</c>.
/// </summary>
public sealed class PSResourceFindOptions
{
    /// <summary>Resource names to search for.</summary>
    public IReadOnlyList<string> Names { get; }
    /// <summary>Version constraint string (PSResourceGet -Version value).</summary>
    public string? Version { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Repository names to search in.</summary>
    public IReadOnlyList<string> Repositories { get; }
    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceFindOptions(IReadOnlyList<string> names, string? version = null, bool prerelease = false, IReadOnlyList<string>? repositories = null)
    {
        Names = names ?? Array.Empty<string>();
        Version = version;
        Prerelease = prerelease;
        Repositories = repositories ?? Array.Empty<string>();
        Credential = null;
    }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceFindOptions(IReadOnlyList<string> names, string? version, bool prerelease, IReadOnlyList<string>? repositories, RepositoryCredential? credential)
    {
        Names = names ?? Array.Empty<string>();
        Version = version;
        Prerelease = prerelease;
        Repositories = repositories ?? Array.Empty<string>();
        Credential = credential;
    }
}

/// <summary>
/// Options for <c>Publish-PSResource</c>.
/// </summary>
public sealed class PSResourcePublishOptions
{
    /// <summary>Path to a module folder (<c>-Path</c>) or a .nupkg file (<c>-NupkgPath</c>).</summary>
    public string Path { get; }
    /// <summary>When true, uses <c>-NupkgPath</c> instead of <c>-Path</c>.</summary>
    public bool IsNupkg { get; }
    /// <summary>Repository to publish to.</summary>
    public string? Repository { get; }
    /// <summary>API key for the repository.</summary>
    public string? ApiKey { get; }
    /// <summary>DestinationPath passed to Publish-PSResource.</summary>
    public string? DestinationPath { get; }
    /// <summary>Skip dependency check.</summary>
    public bool SkipDependenciesCheck { get; }
    /// <summary>Skip module manifest validation.</summary>
    public bool SkipModuleManifestValidate { get; }
    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourcePublishOptions(
        string path,
        bool isNupkg = false,
        string? repository = null,
        string? apiKey = null,
        string? destinationPath = null,
        bool skipDependenciesCheck = false,
        bool skipModuleManifestValidate = false,
        RepositoryCredential? credential = null)
    {
        Path = path;
        IsNupkg = isNupkg;
        Repository = repository;
        ApiKey = apiKey;
        DestinationPath = destinationPath;
        SkipDependenciesCheck = skipDependenciesCheck;
        SkipModuleManifestValidate = skipModuleManifestValidate;
        Credential = credential;
    }
}

/// <summary>
/// Options for <c>Install-PSResource</c>.
/// </summary>
public sealed class PSResourceInstallOptions
{
    /// <summary>Resource name to install.</summary>
    public string Name { get; }
    /// <summary>Version constraint string (PSResourceGet -Version value).</summary>
    public string? Version { get; }
    /// <summary>Repository to install from (optional).</summary>
    public string? Repository { get; }
    /// <summary>Install scope (CurrentUser/AllUsers). Default: CurrentUser.</summary>
    public string Scope { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Whether to reinstall even if already installed.</summary>
    public bool Reinstall { get; }
    /// <summary>Whether to trust repository (avoid prompts).</summary>
    public bool TrustRepository { get; }
    /// <summary>Whether to skip dependency checks.</summary>
    public bool SkipDependencyCheck { get; }
    /// <summary>Whether to accept license prompts.</summary>
    public bool AcceptLicense { get; }
    /// <summary>Whether to suppress non-essential output.</summary>
    public bool Quiet { get; }
    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceInstallOptions(
        string name,
        string? version = null,
        string? repository = null,
        string scope = "CurrentUser",
        bool prerelease = false,
        bool reinstall = false,
        bool trustRepository = true,
        bool skipDependencyCheck = false,
        bool acceptLicense = true,
        bool quiet = true,
        RepositoryCredential? credential = null)
    {
        Name = name;
        Version = version;
        Repository = repository;
        Scope = string.IsNullOrWhiteSpace(scope) ? "CurrentUser" : scope;
        Prerelease = prerelease;
        Reinstall = reinstall;
        TrustRepository = trustRepository;
        SkipDependencyCheck = skipDependencyCheck;
        AcceptLicense = acceptLicense;
        Quiet = quiet;
        Credential = credential;
    }
}

/// <summary>
/// Options for <c>Save-PSResource</c>.
/// </summary>
public sealed class PSResourceSaveOptions
{
    /// <summary>Resource name to save.</summary>
    public string Name { get; }
    /// <summary>Destination path passed to <c>-Path</c>.</summary>
    public string DestinationPath { get; }
    /// <summary>Version constraint string (PSResourceGet -Version value).</summary>
    public string? Version { get; }
    /// <summary>Repository to save from (optional).</summary>
    public string? Repository { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Whether to trust repository (avoid prompts).</summary>
    public bool TrustRepository { get; }
    /// <summary>Whether to skip dependency checks.</summary>
    public bool SkipDependencyCheck { get; }
    /// <summary>Whether to accept license prompts.</summary>
    public bool AcceptLicense { get; }
    /// <summary>Whether to suppress non-essential output.</summary>
    public bool Quiet { get; }
    /// <summary>Optional credential used for repository access.</summary>
    public RepositoryCredential? Credential { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceSaveOptions(
        string name,
        string destinationPath,
        string? version = null,
        string? repository = null,
        bool prerelease = false,
        bool trustRepository = true,
        bool skipDependencyCheck = true,
        bool acceptLicense = true,
        bool quiet = true,
        RepositoryCredential? credential = null)
    {
        Name = name;
        DestinationPath = destinationPath;
        Version = version;
        Repository = repository;
        Prerelease = prerelease;
        TrustRepository = trustRepository;
        SkipDependencyCheck = skipDependencyCheck;
        AcceptLicense = acceptLicense;
        Quiet = quiet;
        Credential = credential;
    }
}

/// <summary>
/// Out-of-process wrapper for PSResourceGet (Find-PSResource / Publish-PSResource).
/// </summary>
public sealed partial class PSResourceGetClient
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new client using the provided runner and logger.
    /// </summary>
    public PSResourceGetClient(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds PowerShell resources using PSResourceGet.
    /// </summary>
    public IReadOnlyList<PSResourceInfo> Find(PSResourceFindOptions options, TimeSpan? timeout = null)
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

        // IMPORTANT:
        // When no repository is specified, PSResourceGet may enumerate all registered repositories
        // and attempt to resolve CredentialInfo from SecretManagement/SecretStore. If SecretStore is
        // locked, this causes Find-PSResource/Save-PSResource/Install-PSResource to fail even for
        // public PSGallery operations. Default to PSGallery to keep builds non-interactive.
        if (repos.Length == 0)
            repos = new[] { "PSGallery" };

        var script = BuildFindScript();
        var args = new List<string>(6)
        {
            EncodeLines(names),
            options.Version ?? string.Empty,
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
            var full = $"Find-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }

        return items;
    }

    /// <summary>
    /// Publishes a PowerShell resource using PSResourceGet.
    /// </summary>
    public void Publish(PSResourcePublishOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Path)) throw new ArgumentException("Path is required.", nameof(options));

        var script = BuildPublishScript();
        var args = new List<string>(9)
        {
            options.Path,
            options.IsNupkg ? "1" : "0",
            options.Repository ?? string.Empty,
            options.ApiKey ?? string.Empty,
            options.DestinationPath ?? string.Empty,
            options.SkipDependenciesCheck ? "1" : "0",
            options.SkipModuleManifestValidate ? "1" : "0",
            options.Credential?.UserName ?? string.Empty,
            options.Credential?.Secret ?? string.Empty
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Publish-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }
    }

    /// <summary>
    /// Installs a PowerShell resource using PSResourceGet.
    /// </summary>
    public void Install(PSResourceInstallOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));

        var script = BuildInstallScript();
        var repository = string.IsNullOrWhiteSpace(options.Repository) ? "PSGallery" : options.Repository!.Trim();
        var args = new List<string>(12)
        {
            options.Name,
            options.Version ?? string.Empty,
            repository,
            options.Scope ?? string.Empty,
            options.Prerelease ? "1" : "0",
            options.Reinstall ? "1" : "0",
            options.TrustRepository ? "1" : "0",
            options.SkipDependencyCheck ? "1" : "0",
            options.AcceptLicense ? "1" : "0",
            options.Quiet ? "1" : "0",
            options.Credential?.UserName ?? string.Empty,
            options.Credential?.Secret ?? string.Empty
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Install-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut))
                _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr))
                _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }
    }

    /// <summary>
    /// Saves a PowerShell resource using PSResourceGet (<c>Save-PSResource</c>).
    /// </summary>
    public IReadOnlyList<PSResourceInfo> Save(PSResourceSaveOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DestinationPath)) throw new ArgumentException("DestinationPath is required.", nameof(options));

        var dest = Path.GetFullPath(options.DestinationPath);
        Directory.CreateDirectory(dest);

        var script = BuildSaveScript();
        var repository = string.IsNullOrWhiteSpace(options.Repository) ? "PSGallery" : options.Repository!.Trim();
        var args = new List<string>(11)
        {
            options.Name,
            options.Version ?? string.Empty,
            repository,
            dest,
            options.Prerelease ? "1" : "0",
            options.TrustRepository ? "1" : "0",
            options.SkipDependencyCheck ? "1" : "0",
            options.AcceptLicense ? "1" : "0",
            options.Quiet ? "1" : "0",
            options.Credential?.UserName ?? string.Empty,
            options.Credential?.Secret ?? string.Empty
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        var items = ParseSaveOutput(result.StdOut);

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Save-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut))
                _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr))
                _logger.Verbose(result.StdErr.Trim());
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }

        return items;
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerForge", "psresourceget");
        Directory.CreateDirectory(tempDir);
        var scriptPath = System.IO.Path.Combine(tempDir, $"psresourceget_{Guid.NewGuid():N}.ps1");
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

    private static IReadOnlyList<PSResourceInfo> ParseFindOutput(string stdout)
    {
        var list = new List<PSResourceInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 7) continue;

            var name = Decode(parts[2]);
            var version = Decode(parts[3]);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
            var repo = EmptyToNull(Decode(parts[4]));
            var author = EmptyToNull(Decode(parts[5]));
            var desc = EmptyToNull(Decode(parts[6]));
            var guid = parts.Length > 7 ? EmptyToNull(Decode(parts[7])) : null;
            list.Add(new PSResourceInfo(name, version, repo, author, desc, guid));
        }
        return list;
    }

    private static IReadOnlyList<PSResourceInfo> ParseSaveOutput(string stdout)
    {
        var list = new List<PSResourceInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::SAVE::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 5) continue;

            var name = Decode(parts[3]);
            var version = Decode(parts[4]);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
            list.Add(new PSResourceInfo(name, version, repository: null, author: null, description: null));
        }
        return list;
    }

    private static string? TryExtractError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFPSRG::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
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

    private static string? EmptyToNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string BuildFindScript()
    {
        return EmbeddedScripts.Load("Scripts/PSResourceGet/Find-PSResource.ps1");
}

    private static string BuildPublishScript()
    {
        return EmbeddedScripts.Load("Scripts/PSResourceGet/Publish-PSResource.ps1");
}

    private static string BuildInstallScript()
    {
        return EmbeddedScripts.Load("Scripts/PSResourceGet/Install-PSResource.ps1");
}

    private static string BuildSaveScript()
    {
        return EmbeddedScripts.Load("Scripts/PSResourceGet/Save-PSResource.ps1");
}
}

