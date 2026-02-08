using PowerForge;
using PowerForge.Cli;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    static string? TryGetOptionValue(string[] argv, string optionName)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
            return ++i < argv.Length ? argv[i] : null;
        }
        return null;
    }

    static string? TryGetProjectRoot(string[] argv)
        => TryGetOptionValue(argv, "--project-root")
           ?? TryGetOptionValue(argv, "--project")
           ?? TryGetOptionValue(argv, "--path");

    static string ResolvePathFromBase(string baseDir, string path)
    {
        var raw = (path ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Path is empty.", nameof(path));

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw));
    }

    static string? ResolvePathFromBaseNullable(string baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var raw = path.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw));
    }

    static void ResolveBuildSpecPaths(ModuleBuildSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(spec.SourcePath))
            spec.SourcePath = ResolvePathFromBase(baseDir, spec.SourcePath);
        spec.StagingPath = ResolvePathFromBaseNullable(baseDir, spec.StagingPath);
        spec.CsprojPath = ResolvePathFromBaseNullable(baseDir, spec.CsprojPath);
    }

    static void ResolveInstallSpecPaths(ModuleInstallSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(spec.StagingPath))
            spec.StagingPath = ResolvePathFromBase(baseDir, spec.StagingPath);
    }

    static void ResolveTestSpecPaths(ModuleTestSuiteSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(spec.ProjectPath))
            spec.ProjectPath = ResolvePathFromBase(baseDir, spec.ProjectPath);
        spec.TestPath = ResolvePathFromBaseNullable(baseDir, spec.TestPath);
    }

    static void ResolvePipelineSpecPaths(ModulePipelineSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
        if (spec.Build is null) return;

        if (!string.IsNullOrWhiteSpace(spec.Build.SourcePath))
            spec.Build.SourcePath = ResolvePathFromBase(baseDir, spec.Build.SourcePath);
        spec.Build.StagingPath = ResolvePathFromBaseNullable(baseDir, spec.Build.StagingPath);
        spec.Build.CsprojPath = ResolvePathFromBaseNullable(baseDir, spec.Build.CsprojPath);
    }

    static string? FindDefaultPipelineConfig(string baseDir)
    {
        var candidates = new[]
        {
            "powerforge.json",
            "powerforge.pipeline.json",
            Path.Combine(".powerforge", "powerforge.json"),
            Path.Combine(".powerforge", "pipeline.json"),
        };

        foreach (var dir in EnumerateSelfAndParents(baseDir))
        {
            foreach (var rel in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    static string? FindDefaultBuildConfig(string baseDir)
    {
        var candidates = new[]
        {
            "powerforge.build.json",
            Path.Combine(".powerforge", "build.json"),
        };

        foreach (var dir in EnumerateSelfAndParents(baseDir))
        {
            foreach (var rel in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore */ }
            }
        }

        return FindDefaultPipelineConfig(baseDir);
    }

    static string? FindDefaultDotNetPublishConfig(string baseDir)
    {
        var candidates = new[]
        {
            "powerforge.dotnetpublish.json",
            Path.Combine(".powerforge", "dotnetpublish.json"),
        };

        foreach (var dir in EnumerateSelfAndParents(baseDir))
        {
            foreach (var rel in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (File.Exists(full)) return full;
                }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    static IEnumerable<string> EnumerateSelfAndParents(string? baseDir)       
    {
        string current;
        try
        {
            current = Path.GetFullPath(string.IsNullOrWhiteSpace(baseDir)
                ? Directory.GetCurrentDirectory()
                : baseDir.Trim().Trim('"'));
        }
        catch
        {
            current = Directory.GetCurrentDirectory();
        }

        while (true)
        {
            yield return current;

            try
            {
                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent)) yield break;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) yield break;
                current = parent;
            }
            catch
            {
                yield break;
            }
        }
    }

    static bool LooksLikePipelineSpec(string fullConfigPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fullConfigPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = doc.RootElement;
            return root.TryGetProperty("Build", out _) || root.TryGetProperty("Segments", out _) || root.TryGetProperty("Install", out _);
        }
        catch
        {
            return false;
        }
    }

    static string ResolveExistingFilePath(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");
        return full;
    }

    static (ModulePipelineSpec Value, string FullPath) LoadPipelineSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModulePipelineSpec, full);
        return (spec, full);
    }

    static (ModuleBuildSpec Value, string FullPath) LoadBuildSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModuleBuildSpec, full);
        return (spec, full);
    }

    static (DotNetPublishSpec Value, string FullPath) LoadDotNetPublishSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.DotNetPublishSpec, full);
        return (spec, full);
    }

    static (ModuleInstallSpec Value, string FullPath) LoadInstallSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModuleInstallSpec, full);
        return (spec, full);
    }

    static (ModuleTestSuiteSpec Value, string FullPath) LoadTestSuiteSpecWithPath(string path)
    {
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.ModuleTestSuiteSpec, full);
        return (spec, full);
    }

    static (string[] Names, string? Version, bool Prerelease, string[] Repositories)? ParseFindArgs(string[] argv)
    {
        var names = new List<string>();
        var repos = new List<string>();
        string? version = null;
        bool prerelease = false;

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--name":
                    if (++i < argv.Length)
                    {
                        foreach (var n in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            names.Add(n.Trim());
                    }
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                case "--repo":
                case "--repository":
                    if (++i < argv.Length) repos.Add(argv[i]);
                    break;
                case "--version":
                    version = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--prerelease":
                    prerelease = true;
                    break;
            }
        }

        if (names.Count == 0) return null;
        return (names.ToArray(), version, prerelease, repos.ToArray());
    }

    static RepositoryPublishRequest? ParsePublishArgs(string[] argv)
    {
        string? path = null;
        string? repositoryName = null;
        string? apiKey = null;
        string? destination = null;
        bool isNupkg = false;
        bool skipDeps = false;
        bool skipManifest = false;

        PublishTool tool = PublishTool.Auto;

        string? repoUri = null;
        string? repoSourceUri = null;
        string? repoPublishUri = null;
        bool repoTrusted = true;
        bool repoTrustedProvided = false;
        int? repoPriority = null;
        RepositoryApiVersion repoApiVersion = RepositoryApiVersion.Auto;
        bool repoApiVersionProvided = false;
        bool ensureRepoRegistered = true;
        bool ensureRepoProvided = false;
        bool unregisterAfterUse = false;

        string? repoCredUser = null;
        string? repoCredSecret = null;
        string? repoCredSecretFile = null;

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--path":
                    path = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                case "--repo":
                case "--repository":
                    repositoryName = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--tool":
                    tool = ParsePublishTool(++i < argv.Length ? argv[i] : null);
                    break;
                case "--apikey":
                case "--api-key":
                    apiKey = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--destination":
                case "--destination-path":
                    destination = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--nupkg":
                    isNupkg = true;
                    break;
                case "--skip-dependencies-check":
                    skipDeps = true;
                    break;
                case "--skip-manifest-validate":
                case "--skip-module-manifest-validate":
                    skipManifest = true;
                    break;

                case "--repo-uri":
                    repoUri = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-source-uri":
                    repoSourceUri = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-publish-uri":
                    repoPublishUri = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-trusted":
                    repoTrusted = true;
                    repoTrustedProvided = true;
                    break;
                case "--repo-untrusted":
                    repoTrusted = false;
                    repoTrustedProvided = true;
                    break;
                case "--repo-priority":
                    if (++i < argv.Length && int.TryParse(argv[i], out var p)) repoPriority = p;
                    break;
                case "--repo-api-version":
                    repoApiVersion = ParseRepositoryApiVersion(++i < argv.Length ? argv[i] : null);
                    repoApiVersionProvided = true;
                    break;
                case "--repo-ensure":
                    ensureRepoRegistered = true;
                    ensureRepoProvided = true;
                    break;
                case "--no-repo-ensure":
                    ensureRepoRegistered = false;
                    ensureRepoProvided = true;
                    break;
                case "--repo-unregister-after-use":
                    unregisterAfterUse = true;
                    break;

                case "--repo-credential-username":
                    repoCredUser = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-credential-secret":
                    repoCredSecret = ++i < argv.Length ? argv[i] : null;
                    break;
                case "--repo-credential-secret-file":
                    repoCredSecretFile = ++i < argv.Length ? argv[i] : null;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(path)) return null;

        string? repositorySecret = null;
        if (!string.IsNullOrWhiteSpace(repoCredSecretFile))
        {
            var full = Path.GetFullPath(repoCredSecretFile!.Trim().Trim('"'));
            repositorySecret = File.ReadAllText(full).Trim();
        }
        else if (!string.IsNullOrWhiteSpace(repoCredSecret))
        {
            repositorySecret = repoCredSecret.Trim();
        }

        var anyRepositoryUriProvided =
            !string.IsNullOrWhiteSpace(repoUri) ||
            !string.IsNullOrWhiteSpace(repoSourceUri) ||
            !string.IsNullOrWhiteSpace(repoPublishUri);

        if (anyRepositoryUriProvided)
        {
            if (string.IsNullOrWhiteSpace(repositoryName))
                throw new InvalidOperationException("Repository name is required when --repo-uri/--repo-source-uri/--repo-publish-uri is provided.");
            if (string.Equals(repositoryName.Trim(), "PSGallery", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Repository name cannot be 'PSGallery' when --repo-uri/--repo-source-uri/--repo-publish-uri is provided.");
        }

        PublishRepositoryConfiguration? repoConfig = null;
        var hasRepoCred = !string.IsNullOrWhiteSpace(repoCredUser) && !string.IsNullOrWhiteSpace(repositorySecret);
        var hasRepoOptions =
            anyRepositoryUriProvided ||
            hasRepoCred ||
            repoPriority.HasValue ||
            repoApiVersionProvided ||
            repoTrustedProvided ||
            ensureRepoProvided ||
            unregisterAfterUse;

        if (hasRepoOptions)
        {
            repoConfig = new PublishRepositoryConfiguration
            {
                Name = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
                Uri = string.IsNullOrWhiteSpace(repoUri) ? null : repoUri.Trim(),
                SourceUri = string.IsNullOrWhiteSpace(repoSourceUri) ? null : repoSourceUri.Trim(),
                PublishUri = string.IsNullOrWhiteSpace(repoPublishUri) ? null : repoPublishUri.Trim(),
                Trusted = repoTrusted,
                Priority = repoPriority,
                ApiVersion = repoApiVersion,
                EnsureRegistered = ensureRepoRegistered,
                UnregisterAfterUse = unregisterAfterUse,
                Credential = hasRepoCred
                    ? new RepositoryCredential { UserName = repoCredUser!.Trim(), Secret = repositorySecret }
                    : null
            };
        }

        return new RepositoryPublishRequest
        {
            Path = path!,
            IsNupkg = isNupkg,
            RepositoryName = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
            Tool = tool,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            Repository = repoConfig,
            DestinationPath = string.IsNullOrWhiteSpace(destination) ? null : destination.Trim(),
            SkipDependenciesCheck = skipDeps,
            SkipModuleManifestValidate = skipManifest
        };
    }

    static PublishTool ParsePublishTool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PublishTool.Auto;

        var v = value.Trim();
        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return PublishTool.Auto;
        if (v.Equals("psresourceget", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("psresource", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("psrg", StringComparison.OrdinalIgnoreCase))
            return PublishTool.PSResourceGet;
        if (v.Equals("powershellget", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("psget", StringComparison.OrdinalIgnoreCase))
            return PublishTool.PowerShellGet;

        throw new InvalidOperationException($"Invalid value for --tool: '{value}'. Expected: auto|psresourceget|powershellget.");
    }

    static RepositoryApiVersion ParseRepositoryApiVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return RepositoryApiVersion.Auto;

        var v = value.Trim();
        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return RepositoryApiVersion.Auto;
        if (v.Equals("v2", StringComparison.OrdinalIgnoreCase) || v.Equals("2", StringComparison.OrdinalIgnoreCase))
            return RepositoryApiVersion.V2;
        if (v.Equals("v3", StringComparison.OrdinalIgnoreCase) || v.Equals("3", StringComparison.OrdinalIgnoreCase))
            return RepositoryApiVersion.V3;

        throw new InvalidOperationException($"Invalid value for --repo-api-version: '{value}'. Expected: auto|v2|v3.");
    }

    static bool IsJsonOutput(string[] argv)
    {
        foreach (var a in argv)
        {
            if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var output = TryGetOptionValue(argv, "--output");
        return string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
    }

    static string[] ParseTargets(string[] argv)
    {
        var list = new List<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            if (a.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(a);
        }

        return list.ToArray();
    }

    static JsonElement? LogsToJsonElement(BufferingLogger? logBuffer)
    {
        if (logBuffer is null) return null;
        if (logBuffer.Entries.Count == 0) return null;
        return CliJson.SerializeToElement(logBuffer.Entries.ToArray(), CliJson.Context.LogEntryArray);
    }

    static ProcessResult RunProcess(string command, IEnumerable<string> args, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            return new ProcessResult(1, string.Empty, "Failed to start PowerShell process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    static string[] BuildPowerShellArgs(string script)
    {
        var args = new List<string> { "-NoProfile", "-NonInteractive" };
        if (OperatingSystem.IsWindows())
        {
            args.Add("-ExecutionPolicy");
            args.Add("Bypass");
        }
        args.Add("-Command");
        args.Add(script);
        return args.ToArray();
    }

    static string QuotePowerShellLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    static string BuildTemplateScript(string scriptPath, string outPath, string projectRoot)
    {
        var escapedScript = QuotePowerShellLiteral(scriptPath);
        var escapedConfig = QuotePowerShellLiteral(outPath);
        var escapedRoot = QuotePowerShellLiteral(projectRoot);

        return string.Join("; ", new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {escapedRoot}",
            "try {",
            "  Import-Module PSPublishModule -Force -ErrorAction Stop",
            $"  $targetJson = {escapedConfig}",
            "  function Invoke-ModuleBuild {",
            "    param([Parameter(ValueFromRemainingArguments = $true)][object[]]$Args)",
            "    $cmd = Get-Command -Name Invoke-ModuleBuild -CommandType Cmdlet -Module PSPublishModule",
            "    & $cmd @Args -JsonOnly -JsonPath $targetJson -NoInteractive",
            "  }",
            "  Set-Alias -Name Build-Module -Value Invoke-ModuleBuild -Scope Local",
            $"  . {escapedScript}",
            "} catch {",
            "  Write-Error $_.Exception.Message",
            "  exit 1",
            "}"
        });
    }

    static void WriteJson(CliJsonEnvelope envelope, Action<Utf8JsonWriter>? writeAdditionalProperties = null)
    {
        CliJsonWriter.Write(envelope, writeAdditionalProperties);
    }

    sealed record ProcessResult(int ExitCode, string Output, string Error);

}

sealed class LogEntry
{
    public string Level { get; }
    public string Message { get; }

    public LogEntry(string level, string message)
    {
        Level = level;
        Message = message;
    }
}

sealed class BufferingLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public List<LogEntry> Entries { get; } = new();

    public void Info(string message) => Entries.Add(new LogEntry("info", message));
    public void Success(string message) => Entries.Add(new LogEntry("success", message));
    public void Warn(string message) => Entries.Add(new LogEntry("warn", message));
    public void Error(string message) => Entries.Add(new LogEntry("error", message));
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Entries.Add(new LogEntry("verbose", message));
    }
}

sealed class CliOptions
{
    public bool Verbose { get; }
    public bool Quiet { get; }
    public bool Diagnostics { get; }
    public bool NoColor { get; }
    public ConsoleView View { get; }

    public CliOptions(bool verbose, bool quiet, bool diagnostics, bool noColor, ConsoleView view)
    {
        Verbose = verbose;
        Quiet = quiet;
        Diagnostics = diagnostics;
        NoColor = noColor;
        View = view;
    }
}

sealed class QuietLogger : ILogger
{
    private readonly ILogger _inner;

    public QuietLogger(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsVerbose => _inner.IsVerbose;

    public void Info(string message) { }
    public void Success(string message) { }
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message) => _inner.Error(message);
    public void Verbose(string message) { }
}

