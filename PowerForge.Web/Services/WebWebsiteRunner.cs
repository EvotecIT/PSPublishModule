using System.Diagnostics;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Resolves and runs website pipelines from source locks or published binary locks.</summary>
public static class WebWebsiteRunner
{
    /// <summary>Runs a website pipeline using the requested engine mode.</summary>
    public static WebWebsiteRunnerResult Run(
        WebWebsiteRunnerOptions options,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var websiteRoot = ResolveRequiredDirectory(options.WebsiteRoot, "Website root");
        var pipelineConfig = ResolveRequiredFile(options.PipelineConfig, "Pipeline config");
        var engineMode = ResolveRequestedEngineMode(options);
        if (!engineMode.Equals("source", StringComparison.Ordinal) &&
            !engineMode.Equals("binary", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported engine mode '{options.EngineMode}'. Supported values: source, binary.");
        }

        if (options.MaintenanceModeNote)
            standardOutput?.Invoke("Maintenance uses the same strict CI mode; the selected pipeline config controls which tasks run.");

        var sessionRoot = CreateSessionRoot(options.RunnerTempPath);
        try
        {
            return engineMode.Equals("source", StringComparison.Ordinal)
                ? RunSourceMode(options, websiteRoot, pipelineConfig, sessionRoot, standardOutput, standardError)
                : RunBinaryMode(options, websiteRoot, pipelineConfig, sessionRoot, standardOutput, standardError);
        }
        finally
        {
            TryDeleteDirectory(sessionRoot);
        }
    }

    private static WebWebsiteRunnerResult RunSourceMode(
        WebWebsiteRunnerOptions options,
        string websiteRoot,
        string pipelineConfig,
        string sessionRoot,
        Action<string>? standardOutput,
        Action<string>? standardError)
    {
        var lockPath = string.IsNullOrWhiteSpace(options.PowerForgeLockPath)
            ? Path.Combine(websiteRoot, ".powerforge", "engine-lock.json")
            : ResolvePath(options.PowerForgeLockPath!, Directory.GetCurrentDirectory());

        var resolvedRepository = NormalizeValue(options.PowerForgeRepository, string.Empty);
        var resolvedRef = NormalizeValue(options.PowerForgeRef, string.Empty);
        if ((string.IsNullOrWhiteSpace(resolvedRepository) || string.IsNullOrWhiteSpace(resolvedRef)) && File.Exists(lockPath))
        {
            var engineLock = WebEngineLockFile.Read(lockPath, WebJson.Options);
            if (string.IsNullOrWhiteSpace(resolvedRepository))
                resolvedRepository = engineLock.Repository;
            if (string.IsNullOrWhiteSpace(resolvedRef))
                resolvedRef = engineLock.Ref;
        }

        if (string.IsNullOrWhiteSpace(resolvedRepository) || string.IsNullOrWhiteSpace(resolvedRef))
            throw new InvalidOperationException($"Provide PowerForge repository/ref overrides or commit a valid engine lock file at '{lockPath}'.");

        var finalRepository = NormalizeGitHubRepository(
            string.IsNullOrWhiteSpace(options.PowerForgeRepositoryOverride) ? resolvedRepository : options.PowerForgeRepositoryOverride!);
        var finalRef = NormalizeValue(
            string.IsNullOrWhiteSpace(options.PowerForgeRefOverride) ? resolvedRef : options.PowerForgeRefOverride,
            string.Empty);
        if (!WebEngineLockFile.IsCommitSha(finalRef))
            throw new InvalidOperationException($"PowerForge ref must be an immutable commit SHA (40/64 hex): '{finalRef}'.");

        var extractRoot = Path.Combine(sessionRoot, "engine");
        RunProcess(
            "git",
            null,
            new[] { "clone", "--filter=blob:none", "--no-checkout", "--quiet", GetGitCloneUrl(finalRepository), extractRoot },
            CreateGitHubGitEnvironment(options.GitHubToken),
            standardOutput,
            standardError,
            $"git clone --filter=blob:none --no-checkout --quiet https://github.com/{finalRepository}.git {extractRoot}");
        RunProcess(
            "git",
            null,
            new[] { "-C", extractRoot, "fetch", "--depth", "1", "origin", finalRef },
            CreateGitHubGitEnvironment(options.GitHubToken),
            standardOutput,
            standardError);
        RunProcess(
            "git",
            null,
            new[] { "-C", extractRoot, "-c", "advice.detachedHead=false", "checkout", "--force", "FETCH_HEAD" },
            null,
            standardOutput,
            standardError);

        var projectPath = ResolveUniqueFile(extractRoot, "PowerForge.Web.Cli.csproj");

        RunProcess(
            "dotnet",
            null,
            new[]
            {
                "run", "--framework", "net10.0", "--project", projectPath, "--",
                "pipeline", "--config", pipelineConfig, "--mode", NormalizeValue(options.PipelineMode, "ci")
            },
            null,
            standardOutput,
            standardError);

        return new WebWebsiteRunnerResult
        {
            EngineMode = "source",
            WebsiteRoot = websiteRoot,
            PipelineConfig = pipelineConfig,
            PipelineMode = NormalizeValue(options.PipelineMode, "ci"),
            Repository = finalRepository,
            Ref = finalRef,
            LaunchedPath = projectPath
        };
    }

    private static WebWebsiteRunnerResult RunBinaryMode(
        WebWebsiteRunnerOptions options,
        string websiteRoot,
        string pipelineConfig,
        string sessionRoot,
        Action<string>? standardOutput,
        Action<string>? standardError)
    {
        var toolLock = ResolveBinaryToolSpec(options, websiteRoot);
        var repository = NormalizeGitHubRepository(toolLock.Repository);
        var release = ResolveGitHubRelease(repository, toolLock.Tag, options.GitHubToken);
        var resolvedAsset = ResolveReleaseAsset(
            release.Assets,
            toolLock.Target,
            toolLock.Asset,
            GetCurrentRuntimeIdentifier());

        var assetPath = Path.Combine(sessionRoot, resolvedAsset.Name);
        DownloadFile(resolvedAsset.DownloadUrl, assetPath, options.GitHubToken);
        VerifySha256IfPresent(assetPath, string.IsNullOrWhiteSpace(toolLock.Sha256) ? resolvedAsset.Sha256 : toolLock.Sha256);

        var extractRoot = Path.Combine(sessionRoot, "tool");
        ExtractArchive(assetPath, extractRoot);
        var executablePath = ResolveExecutablePath(extractRoot, toolLock.Target, toolLock.BinaryPath);
        EnsureExecutableBit(executablePath);

        RunProcess(
            executablePath,
            null,
            new[] { "pipeline", "--config", pipelineConfig, "--mode", NormalizeValue(options.PipelineMode, "ci") },
            null,
            standardOutput,
            standardError);

        return new WebWebsiteRunnerResult
        {
            EngineMode = "binary",
            WebsiteRoot = websiteRoot,
            PipelineConfig = pipelineConfig,
            PipelineMode = NormalizeValue(options.PipelineMode, "ci"),
            Repository = repository,
            Tag = toolLock.Tag,
            Asset = resolvedAsset.Name,
            LaunchedPath = executablePath
        };
    }

    internal static string ResolveRequestedEngineMode(WebWebsiteRunnerOptions options)
    {
        var configuredMode = NormalizeValue(options.EngineMode, string.Empty).ToLowerInvariant();
        if (configuredMode.Equals("source", StringComparison.Ordinal) || configuredMode.Equals("binary", StringComparison.Ordinal))
            return configuredMode;
        if (!string.IsNullOrWhiteSpace(configuredMode))
            return configuredMode;

        if (!string.IsNullOrWhiteSpace(options.PowerForgeWebTag) || !string.IsNullOrWhiteSpace(options.PowerForgeToolLockPath))
            return "binary";

        return "source";
    }

    internal static string GetCurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : throw new InvalidOperationException("Binary mode supports only Windows, Linux, and macOS runners.");

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new InvalidOperationException($"Binary mode does not support architecture '{RuntimeInformation.ProcessArchitecture}'.")
        };

        return $"{os}-{architecture}";
    }

    internal static WebGitHubReleaseAsset ResolveReleaseAsset(
        IReadOnlyList<WebGitHubReleaseAsset> assets,
        string target,
        string? configuredAsset,
        string runtimeIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(configuredAsset))
        {
            var explicitAsset = assets.FirstOrDefault(asset => asset.Name.Equals(configuredAsset.Trim(), StringComparison.Ordinal));
            if (explicitAsset is null)
                throw new InvalidOperationException($"GitHub release does not contain asset '{configuredAsset}'.");
            return explicitAsset;
        }

        var matches = assets
            .Where(asset =>
                asset.Name.StartsWith(target + "-", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.Contains("-" + runtimeIdentifier + "-", StringComparison.OrdinalIgnoreCase) &&
                (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                 asset.Name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length == 0)
            throw new InvalidOperationException($"GitHub release does not contain a supported asset for target '{target}' and runtime '{runtimeIdentifier}'.");

        if (matches.Length == 1)
            return matches[0];

        var singleContained = matches.Where(asset => asset.Name.Contains("-SingleContained", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (singleContained.Length == 1)
            return singleContained[0];

        throw new InvalidOperationException($"GitHub release contains multiple matching assets for target '{target}' and runtime '{runtimeIdentifier}'.");
    }

    private static WebToolLockSpec ResolveBinaryToolSpec(WebWebsiteRunnerOptions options, string websiteRoot)
    {
        if (!string.IsNullOrWhiteSpace(options.PowerForgeWebTag))
        {
            return WebToolLockFile.Normalize(new WebToolLockSpec
            {
                Repository = WebToolLockFile.DefaultRepository,
                Target = WebToolLockFile.DefaultTarget,
                Tag = options.PowerForgeWebTag
            });
        }

        var toolLockPath = string.IsNullOrWhiteSpace(options.PowerForgeToolLockPath)
            ? Path.Combine(websiteRoot, ".powerforge", "tool-lock.json")
            : ResolvePath(options.PowerForgeToolLockPath!, Directory.GetCurrentDirectory());

        var toolLock = WebToolLockFile.Read(toolLockPath, WebJson.Options);
        var issues = WebToolLockFile.Validate(toolLock);
        if (issues.Length > 0)
            throw new InvalidOperationException($"Tool lock is invalid: {string.Join(" ", issues)}");
        return toolLock;
    }

    private static WebGitHubRelease ResolveGitHubRelease(string repository, string tag, string? token)
    {
        using var http = CreateGitHubHttpClient(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}");
        using var response = http.Send(request);
        response.EnsureSuccessStatusCode();

        using var contentStream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(contentStream);
        if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"GitHub release '{tag}' in '{repository}' did not return an assets array.");

        var releaseAssets = new List<WebGitHubReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
            releaseAssets.Add(new WebGitHubReleaseAsset
            {
                Name = name,
                DownloadUrl = url,
                Sha256 = NormalizeGitHubDigest(digest)
            });
        }

        return new WebGitHubRelease
        {
            Repository = repository,
            Tag = tag,
            Assets = releaseAssets
        };
    }

    private static void DownloadFile(string url, string destinationPath, string? token)
    {
        using var http = CreateGitHubHttpClient(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var source = response.Content.ReadAsStream();
        using var destination = File.Create(destinationPath);
        source.CopyTo(destination);
    }

    private static HttpClient CreateGitHubHttpClient(string? token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForgeWebCli", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return client;
    }

    private static string GetGitCloneUrl(string repository)
    {
        return $"https://github.com/{repository}.git";
    }

    private static void ExtractArchive(string archivePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destinationPath, overwriteFiles: true);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destinationPath, overwriteFiles: true);
            return;
        }

        throw new InvalidOperationException($"Unsupported archive format: {archivePath}");
    }

    private static string ResolveExecutablePath(string extractRoot, string target, string? binaryPath)
    {
        if (!string.IsNullOrWhiteSpace(binaryPath))
        {
            var resolvedBinaryPath = ResolvePath(binaryPath, extractRoot);
            if (!File.Exists(resolvedBinaryPath))
                throw new InvalidOperationException($"Configured binaryPath was not found after extraction: {resolvedBinaryPath}");
            return resolvedBinaryPath;
        }

        var matches = Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                var baseName = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                return fileName.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                       fileName.Equals(target + ".exe", StringComparison.OrdinalIgnoreCase) ||
                       (string.IsNullOrEmpty(extension) && baseName.Equals(target, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Unable to locate executable for target '{target}' under '{extractRoot}'."),
            _ => throw new InvalidOperationException($"Multiple executables matched target '{target}' under '{extractRoot}'.")
        };
    }

    private static void EnsureExecutableBit(string executablePath)
    {
        if (OperatingSystem.IsWindows())
            return;

        var current = File.GetUnixFileMode(executablePath);
        var updated = current |
                      UnixFileMode.UserExecute |
                      UnixFileMode.GroupExecute |
                      UnixFileMode.OtherExecute;
        if (updated != current)
            File.SetUnixFileMode(executablePath, updated);
    }

    private static void RunProcess(
        string fileName,
        string? workingDirectory,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        Action<string>? standardOutput,
        Action<string>? standardError,
        string? displayCommand = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
        }

        using var process = new Process { StartInfo = startInfo };
        var outputBuffer = new List<string>();
        var errorBuffer = new List<string>();

        process.Start();
        var outputTask = Task.Run(() => ReadLines(process.StandardOutput, outputBuffer, standardOutput));
        var errorTask = Task.Run(() => ReadLines(process.StandardError, errorBuffer, standardError));
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);

        if (process.ExitCode == 0)
            return;

        var tail = errorBuffer.Count > 0
            ? string.Join(Environment.NewLine, errorBuffer.TakeLast(10))
            : string.Join(Environment.NewLine, outputBuffer.TakeLast(10));
        var commandText = string.IsNullOrWhiteSpace(displayCommand)
            ? $"{fileName} {string.Join(" ", arguments)}"
            : displayCommand;
        throw new InvalidOperationException($"Command failed ({process.ExitCode}): {commandText}{(string.IsNullOrWhiteSpace(tail) ? string.Empty : Environment.NewLine + tail)}");
    }

    private static void ReadLines(StreamReader reader, List<string> buffer, Action<string>? callback)
    {
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;

            buffer.Add(line);
            callback?.Invoke(line);
        }
    }

    private static string ResolveUniqueFile(string rootPath, string fileName)
    {
        var matches = Directory.EnumerateFiles(rootPath, fileName, SearchOption.AllDirectories).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Unable to locate '{fileName}' under '{rootPath}'."),
            _ => throw new InvalidOperationException($"Multiple '{fileName}' files were found under '{rootPath}'.")
        };
    }

    private static string ResolveRequiredDirectory(string path, string description)
    {
        var resolved = ResolvePath(path, Directory.GetCurrentDirectory());
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException($"{description} not found: {resolved}");
        return resolved;
    }

    private static string ResolveRequiredFile(string path, string description)
    {
        var resolved = ResolvePath(path, Directory.GetCurrentDirectory());
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"{description} not found: {resolved}");
        return resolved;
    }

    private static string ResolvePath(string path, string basePath)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static string NormalizeGitHubRepository(string repository)
    {
        var trimmed = NormalizeValue(repository, string.Empty);
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("GitHub repository is required.");

        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["git@github.com:".Length..];
        else if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Only github.com repositories are supported: '{repository}'.");
            trimmed = uri.AbsolutePath.Trim('/');
        }

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            throw new InvalidOperationException($"GitHub repository must be 'owner/repo' or a github.com URL: '{repository}'.");

        return $"{parts[0]}/{parts[1]}";
    }

    private static string CreateSessionRoot(string? configuredRoot)
    {
        var tempRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Environment.GetEnvironmentVariable("RUNNER_TEMP")
            : configuredRoot;
        if (string.IsNullOrWhiteSpace(tempRoot))
            tempRoot = Path.GetTempPath();

        var fullRoot = Path.GetFullPath(tempRoot);
        var sessionRoot = Path.Combine(fullRoot, "powerforge-website-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        return sessionRoot;
    }

    private static IReadOnlyDictionary<string, string?>? CreateGitHubGitEnvironment(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var basicToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"x-access-token:{token.Trim()}"));

        return new Dictionary<string, string?>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basicToken}"
        };
    }

    private static void VerifySha256IfPresent(string assetPath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return;

        var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
        if (normalizedExpected.Length != 64 || normalizedExpected.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException($"tool lock sha256 must be 64 hex characters: '{expectedSha256}'.");

        using var stream = File.OpenRead(assetPath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(normalizedExpected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Downloaded asset SHA-256 mismatch for '{Path.GetFileName(assetPath)}'. Expected {normalizedExpected} but got {actual}.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeGitHubDigest(string? digest)
    {
        var normalized = NormalizeValue(digest, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        const string prefix = "sha256:";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];

        return normalized.Trim().ToLowerInvariant();
    }

    internal sealed class WebGitHubRelease
    {
        public string Repository { get; init; } = string.Empty;
        public string Tag { get; init; } = string.Empty;
        public IReadOnlyList<WebGitHubReleaseAsset> Assets { get; init; } = Array.Empty<WebGitHubReleaseAsset>();
    }

    internal sealed class WebGitHubReleaseAsset
    {
        public string Name { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}
