using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private sealed class GitSyncRequest
    {
        public string RepoInput { get; init; } = string.Empty;
        public string? RepoBaseUrl { get; init; }
        public string Repo { get; init; } = string.Empty;
        public string DestinationFull { get; init; } = string.Empty;
        public string? Reference { get; init; }
        public bool Clean { get; init; }
        public bool FetchTags { get; init; }
        public int Depth { get; init; }
        public int TimeoutSeconds { get; init; }
        public int Retry { get; init; }
        public int RetryDelayMs { get; init; }
        public string AuthType { get; init; } = "auto";
        public string? AuthHeader { get; init; }
        public string[] SparseCheckout { get; init; } = Array.Empty<string>();
        public bool Submodules { get; init; }
        public bool SubmodulesRecursive { get; init; }
        public int SubmoduleDepth { get; init; }
        public string BaseDirectory { get; init; } = string.Empty;
    }

    private sealed class GitSyncManifestEntry
    {
        public string RepoInput { get; init; } = string.Empty;
        public string? RepoBaseUrl { get; init; }
        public string Repo { get; init; } = string.Empty;
        public string Destination { get; init; } = string.Empty;
        public string AuthType { get; init; } = "auto";
        public string? RequestedReference { get; init; }
        public string? ResolvedReference { get; init; }
        public string? ResolvedCommit { get; init; }
    }

    private sealed class GitSyncExecutionResult
    {
        public string? DisplayReference { get; init; }
        public string? Commit { get; init; }
    }

    private sealed class GitSyncLockEntry
    {
        public string? RepoInput { get; init; }
        public string? Repo { get; init; }
        public string Destination { get; init; } = string.Empty;
        public string Commit { get; init; } = string.Empty;
    }

    private static void ExecuteGitSync(
        JsonElement step,
        string baseDir,
        WebConsoleLogger? logger,
        WebPipelineStepResult stepResult)
    {
        var hasInlineToken = HasInlineGitToken(step);
        if (hasInlineToken)
        {
            logger?.Warn("[PFWEB.GITSYNC.SECURITY] git-sync detected inline 'token' value in pipeline configuration. Prefer tokenEnv + CI secrets.");
        }

        var requests = ResolveGitSyncRequests(step, baseDir);
        if (requests.Length == 0)
            throw new InvalidOperationException("git-sync has no repositories to sync.");
        var manifestPath = ResolveGitSyncManifestPath(step, baseDir);
        var lockMode = NormalizeGitLockMode(GetString(step, "lockMode") ?? GetString(step, "lock-mode"));
        var lockPath = ResolveGitSyncLockPath(step, baseDir, lockMode);
        var lockEntries = lockMode == "verify"
            ? LoadGitSyncLock(lockPath!)
            : new Dictionary<string, GitSyncLockEntry>(StringComparer.OrdinalIgnoreCase);
        var lockUpdates = lockMode == "update"
            ? new List<GitSyncLockEntry>()
            : null;

        var summaries = new List<string>();
        var manifestEntries = new List<GitSyncManifestEntry>();
        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            string? lockedCommit = null;
            if (lockMode == "verify")
            {
                lockedCommit = ResolveLockedCommit(lockEntries, request, i);
                if (string.IsNullOrWhiteSpace(request.Reference))
                    request = WithReference(request, lockedCommit!);
                else if (!string.Equals(request.Reference.Trim(), lockedCommit, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"git-sync verify mode: repos[{i}] requested ref '{request.Reference}' does not match lock commit '{lockedCommit}'.");
            }

            var execution = ExecuteGitSyncRequest(request);
            if (lockMode == "verify")
            {
                var resolvedCommit = execution.Commit?.Trim();
                if (string.IsNullOrWhiteSpace(resolvedCommit) ||
                    !string.Equals(resolvedCommit, lockedCommit, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"git-sync verify mode: repos[{i}] resolved commit '{resolvedCommit ?? "unknown"}' does not match lock commit '{lockedCommit}'.");
                }
            }

            if (lockUpdates is not null && !string.IsNullOrWhiteSpace(execution.Commit))
            {
                lockUpdates.Add(new GitSyncLockEntry
                {
                    RepoInput = request.RepoInput,
                    Repo = request.Repo,
                    Destination = request.DestinationFull,
                    Commit = execution.Commit!
                });
            }

            var summary = string.IsNullOrWhiteSpace(execution.DisplayReference)
                ? $"{request.RepoInput} -> {request.DestinationFull}"
                : $"{request.RepoInput} -> {request.DestinationFull} ({execution.DisplayReference})";
            summaries.Add(summary);

            manifestEntries.Add(new GitSyncManifestEntry
            {
                RepoInput = request.RepoInput,
                RepoBaseUrl = request.RepoBaseUrl,
                Repo = request.Repo,
                Destination = request.DestinationFull,
                AuthType = request.AuthType,
                RequestedReference = request.Reference,
                ResolvedReference = execution.DisplayReference,
                ResolvedCommit = execution.Commit
            });
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
            WriteGitSyncManifest(manifestPath, manifestEntries);
        if (lockUpdates is { Count: > 0 })
            WriteGitSyncLock(lockPath!, lockUpdates);

        stepResult.Success = true;
        var message = summaries.Count == 1
            ? $"git-sync ok: {summaries[0]}"
            : $"git-sync ok: synchronized {summaries.Count} repositories.";
        if (!string.IsNullOrWhiteSpace(manifestPath))
            message += $" manifest={manifestPath}";
        if (!string.IsNullOrWhiteSpace(lockPath))
            message += $" lock={lockPath} mode={lockMode}";
        if (hasInlineToken)
            message += " warning=[PFWEB.GITSYNC.SECURITY]";
        stepResult.Message = message;
    }

    private static bool HasInlineGitToken(JsonElement step)
    {
        if (!string.IsNullOrWhiteSpace(GetString(step, "token")))
            return true;

        var entries = GetArrayOfObjects(step, "repos") ?? GetArrayOfObjects(step, "repositories");
        if (entries is null || entries.Length == 0)
            return false;

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(GetString(entry, "token")))
                return true;
        }

        return false;
    }

    private static GitSyncRequest[] ResolveGitSyncRequests(JsonElement step, string baseDir)
    {
        var entries = GetArrayOfObjects(step, "repos") ?? GetArrayOfObjects(step, "repositories");
        if (entries is null || entries.Length == 0)
            return new[] { ParseGitSyncRequest(step, step, baseDir, null) };

        var requests = new List<GitSyncRequest>();
        for (var index = 0; index < entries.Length; index++)
            requests.Add(ParseGitSyncRequest(entries[index], step, baseDir, index));
        return requests.ToArray();
    }

    private static GitSyncRequest ParseGitSyncRequest(JsonElement source, JsonElement defaults, string baseDir, int? index)
    {
        var repoValue = GetStringFrom(source, defaults, "repo", "repository", "url");
        var repoBaseUrl = GetStringFrom(source, defaults,
            "repoBaseUrl", "repo-base-url",
            "repositoryBaseUrl", "repository-base-url",
            "repoHost", "repo-host");
        var destinationValue = GetStringFrom(source, defaults, "destination", "dest", "path");
        if (string.IsNullOrWhiteSpace(repoValue))
            throw new InvalidOperationException(index.HasValue
                ? $"git-sync repos[{index.Value}] requires repo."
                : "git-sync requires repo.");
        if (string.IsNullOrWhiteSpace(destinationValue))
            throw new InvalidOperationException(index.HasValue
                ? $"git-sync repos[{index.Value}] requires destination."
                : "git-sync requires destination.");

        var reference = GetStringFrom(source, defaults, "ref", "branch", "tag", "commit");
        var clean = GetBoolFrom(source, defaults, "clean") ?? false;
        var fetchTags = GetBoolFrom(source, defaults, "fetchTags", "fetch-tags") ?? false;
        var depth = GetIntFrom(source, defaults, "depth") ?? 0;
        var timeoutSeconds = GetIntFrom(source, defaults, "timeoutSeconds", "timeout-seconds") ?? 600;
        var retry = GetIntFrom(source, defaults, "retry", "retries", "retryCount", "retry-count") ?? 0;
        var retryDelayMs = GetIntFrom(source, defaults, "retryDelayMs", "retry-delay-ms", "retryDelay", "retry-delay") ?? 500;
        var tokenEnv = GetStringFrom(source, defaults, "tokenEnv", "token-env") ?? "GITHUB_TOKEN";
        var token = GetStringFrom(source, defaults, "token");
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable(tokenEnv);
        var username = GetStringFrom(source, defaults, "username") ?? "x-access-token";
        var authTypeRaw = GetStringFrom(source, defaults, "authType", "auth-type", "auth", "authentication");
        var authType = NormalizeGitAuthType(authTypeRaw, index);
        var authHeader = ResolveGitAuthHeader(authType, token, username, index);
        var sparseCheckout = GetStringArrayFrom(source, defaults, "sparseCheckout", "sparse-checkout");
        if (sparseCheckout is null || sparseCheckout.Length == 0)
        {
            sparseCheckout = CliPatternHelper.SplitPatterns(
                GetStringFrom(source, defaults, "sparsePaths", "sparse-paths"));
        }

        var submodules = GetBoolFrom(source, defaults, "submodules", "submodule") ?? false;
        var submodulesRecursive = GetBoolFrom(source, defaults,
                                      "submodulesRecursive", "submodules-recursive",
                                      "recursiveSubmodules", "recursive-submodules") ??
                                  submodules;
        var submoduleDepth = GetIntFrom(source, defaults, "submoduleDepth", "submodule-depth") ?? 0;

        var destination = ResolvePath(baseDir, destinationValue);
        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException(index.HasValue
                ? $"git-sync repos[{index.Value}] has invalid destination."
                : "git-sync has invalid destination.");

        if (timeoutSeconds <= 0)
            timeoutSeconds = 600;
        if (retry < 0)
            retry = 0;
        if (retryDelayMs < 0)
            retryDelayMs = 0;

        return new GitSyncRequest
        {
            RepoInput = repoValue!,
            RepoBaseUrl = repoBaseUrl,
            Repo = NormalizeGitRepo(repoValue!, baseDir, repoBaseUrl, authType),
            DestinationFull = Path.GetFullPath(destination),
            Reference = reference,
            Clean = clean,
            FetchTags = fetchTags,
            Depth = depth,
            TimeoutSeconds = timeoutSeconds,
            Retry = retry,
            RetryDelayMs = retryDelayMs,
            AuthType = authType,
            AuthHeader = authHeader,
            SparseCheckout = sparseCheckout,
            Submodules = submodules,
            SubmodulesRecursive = submodulesRecursive,
            SubmoduleDepth = submoduleDepth,
            BaseDirectory = baseDir
        };
    }

    private static GitSyncExecutionResult ExecuteGitSyncRequest(GitSyncRequest request)
    {
        if (request.Clean && Directory.Exists(request.DestinationFull))
            Directory.Delete(request.DestinationFull, recursive: true);

        var gitFolder = Path.Combine(request.DestinationFull, ".git");
        var cloned = false;
        if (!Directory.Exists(gitFolder))
        {
            var parent = Path.GetDirectoryName(request.DestinationFull);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            var cloneArgs = new List<string> { "clone", request.Repo, request.DestinationFull };
            if (!request.FetchTags)
                cloneArgs.Add("--no-tags");
            if (request.Depth > 0)
            {
                cloneArgs.Add("--depth");
                cloneArgs.Add(request.Depth.ToString());
            }

            var clone = RunGitCommandWithRetry(
                request.BaseDirectory,
                cloneArgs,
                request.AuthHeader,
                request.TimeoutSeconds,
                request.Retry,
                request.RetryDelayMs);
            if (clone.ExitCode != 0)
            {
                var preview = FirstNonEmptyLine(clone.Error, clone.Output) ?? "unknown error";
                throw new InvalidOperationException($"git-sync clone failed: {preview}");
            }

            cloned = true;
        }
        else
        {
            var fetchArgs = new List<string> { "-C", request.DestinationFull, "fetch", "--prune", "origin" };
            if (request.FetchTags)
                fetchArgs.Add("--tags");
            var fetch = RunGitCommandWithRetry(
                request.BaseDirectory,
                fetchArgs,
                request.AuthHeader,
                request.TimeoutSeconds,
                request.Retry,
                request.RetryDelayMs);
            if (fetch.ExitCode != 0)
            {
                var preview = FirstNonEmptyLine(fetch.Error, fetch.Output) ?? "unknown error";
                throw new InvalidOperationException($"git-sync fetch failed: {preview}");
            }
        }

        if (request.SparseCheckout.Length > 0)
            ConfigureSparseCheckout(request.DestinationFull, request.SparseCheckout, request.AuthHeader, request.TimeoutSeconds, request.Retry, request.RetryDelayMs);

        if (!string.IsNullOrWhiteSpace(request.Reference))
            CheckoutReference(request.DestinationFull, request.Reference!, request.AuthHeader, request.TimeoutSeconds, request.Retry, request.RetryDelayMs);
        else if (!cloned)
            TryFastForwardPull(request.DestinationFull, request.AuthHeader, request.TimeoutSeconds, request.Retry, request.RetryDelayMs);

        if (request.Submodules)
            UpdateSubmodules(request.DestinationFull, request.SubmodulesRecursive, request.SubmoduleDepth, request.AuthHeader, request.TimeoutSeconds, request.Retry, request.RetryDelayMs);

        return new GitSyncExecutionResult
        {
            DisplayReference = ResolveHeadReference(request.DestinationFull, request.AuthHeader, request.TimeoutSeconds, request.Retry, request.RetryDelayMs),
            Commit = ResolveHeadCommit(request.DestinationFull, request.AuthHeader, request.TimeoutSeconds, request.Retry, request.RetryDelayMs)
        };
    }

    private static string NormalizeGitRepo(string repo, string baseDir, string? repoBaseUrl, string authType)
    {
        var trimmed = repo.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains('\\', StringComparison.Ordinal))
            return trimmed;

        if (Path.IsPathRooted(trimmed) ||
            trimmed.StartsWith("./", StringComparison.Ordinal) ||
            trimmed.StartsWith("../", StringComparison.Ordinal) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'))
            return trimmed;

        var resolved = ResolvePath(baseDir, trimmed);
        if (!string.IsNullOrWhiteSpace(resolved) &&
            (Directory.Exists(resolved) || File.Exists(resolved)))
            return trimmed;

        var slashCount = 0;
        foreach (var ch in trimmed)
        {
            if (ch == '/')
                slashCount++;
        }

        var shorthandCandidate = slashCount >= 1 &&
                                 !trimmed.StartsWith("/", StringComparison.Ordinal) &&
                                 !trimmed.Contains(" ", StringComparison.Ordinal);
        if (shorthandCandidate)
        {
            if (!string.IsNullOrWhiteSpace(repoBaseUrl))
                return ResolveRepoFromBaseUrl(trimmed, repoBaseUrl, baseDir, authType);

            if (slashCount == 1)
                return ResolveRepoFromBaseUrl(trimmed, repoBaseUrl, baseDir, authType);
        }

        return trimmed;
    }

    private static string ResolveRepoFromBaseUrl(string repoShorthand, string? repoBaseUrl, string baseDir, string authType)
    {
        var withGitSuffix = repoShorthand.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repoShorthand
            : $"{repoShorthand}.git";
        var effectiveBase = string.IsNullOrWhiteSpace(repoBaseUrl)
            ? "https://github.com"
            : repoBaseUrl.Trim();
        var useSsh = authType.Equals("ssh", StringComparison.OrdinalIgnoreCase);

        if (Uri.TryCreate(effectiveBase, UriKind.Absolute, out var absoluteBaseUri))
        {
            if (absoluteBaseUri.IsFile)
                return BuildRepoPathFromBase(absoluteBaseUri.LocalPath, withGitSuffix, baseDir);

            if (useSsh)
                return BuildSshRepoFromBaseUri(absoluteBaseUri, withGitSuffix);

            if (absoluteBaseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                absoluteBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return $"{effectiveBase.TrimEnd('/')}/{withGitSuffix}";
        }

        if (LooksLikeHostName(effectiveBase))
            return useSsh
                ? BuildSshRepoFromHostString(effectiveBase, withGitSuffix)
                : $"https://{effectiveBase.TrimEnd('/')}/{withGitSuffix}";

        return BuildRepoPathFromBase(effectiveBase, withGitSuffix, baseDir);
    }

    private static string BuildSshRepoFromBaseUri(Uri baseUri, string withGitSuffix)
    {
        var user = string.IsNullOrWhiteSpace(baseUri.UserInfo)
            ? "git"
            : baseUri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries)[0];
        if (string.IsNullOrWhiteSpace(user))
            user = "git";

        var suffix = BuildSshRepoSuffix(baseUri.AbsolutePath, withGitSuffix);
        if (baseUri.IsDefaultPort || baseUri.Port <= 0)
            return $"{user}@{baseUri.Host}:{suffix}";
        return $"ssh://{user}@{baseUri.Host}:{baseUri.Port}/{suffix}";
    }

    private static string BuildSshRepoFromHostString(string baseHost, string withGitSuffix)
    {
        if (!Uri.TryCreate("https://" + baseHost.Trim(), UriKind.Absolute, out var normalized))
            return $"git@{baseHost.TrimEnd('/')}:{withGitSuffix}";

        return BuildSshRepoFromBaseUri(normalized, withGitSuffix);
    }

    private static string BuildSshRepoSuffix(string absolutePath, string withGitSuffix)
    {
        var prefix = (absolutePath ?? string.Empty).Trim('/');
        return string.IsNullOrWhiteSpace(prefix)
            ? withGitSuffix
            : $"{prefix}/{withGitSuffix}";
    }

    private static string BuildRepoPathFromBase(string basePath, string withGitSuffix, string baseDir)
    {
        var resolvedBase = ResolvePath(baseDir, basePath);
        if (string.IsNullOrWhiteSpace(resolvedBase))
            resolvedBase = basePath;
        var normalizedRelative = withGitSuffix
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(resolvedBase, normalizedRelative));
    }

    private static bool LooksLikeHostName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.Contains("\\", StringComparison.Ordinal) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.StartsWith(".", StringComparison.Ordinal))
            return false;
        if (trimmed.Contains(" ", StringComparison.Ordinal))
            return false;

        if (!Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var uri))
            return false;
        return !string.IsNullOrWhiteSpace(uri.Host) &&
               uri.Host.Contains(".", StringComparison.Ordinal);
    }

    private static string? GetStringFrom(JsonElement source, JsonElement defaults, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(source, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        foreach (var name in names)
        {
            var value = GetString(defaults, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool? GetBoolFrom(JsonElement source, JsonElement defaults, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetBool(source, name);
            if (value.HasValue)
                return value;
        }

        foreach (var name in names)
        {
            var value = GetBool(defaults, name);
            if (value.HasValue)
                return value;
        }

        return null;
    }

    private static int? GetIntFrom(JsonElement source, JsonElement defaults, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetInt(source, name);
            if (value.HasValue)
                return value;
        }

        foreach (var name in names)
        {
            var value = GetInt(defaults, name);
            if (value.HasValue)
                return value;
        }

        return null;
    }

    private static string[]? GetStringArrayFrom(JsonElement source, JsonElement defaults, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetArrayOfStrings(source, name);
            if (value is { Length: > 0 })
                return value;
        }

        foreach (var name in names)
        {
            var value = GetArrayOfStrings(defaults, name);
            if (value is { Length: > 0 })
                return value;
        }

        return null;
    }

    private static string? ResolveGitSyncManifestPath(JsonElement step, string baseDir)
    {
        var writeManifest = GetBool(step, "writeManifest") ?? GetBool(step, "write-manifest") ?? false;
        var manifestPath = GetString(step, "manifestPath") ?? GetString(step, "manifest-path") ?? GetString(step, "manifest");
        if (!writeManifest && string.IsNullOrWhiteSpace(manifestPath))
            return null;

        var effectivePath = string.IsNullOrWhiteSpace(manifestPath)
            ? Path.Combine(".powerforge", "git-sync-manifest.json")
            : manifestPath!;
        var resolved = ResolvePath(baseDir, effectivePath);
        return string.IsNullOrWhiteSpace(resolved)
            ? null
            : Path.GetFullPath(resolved);
    }

    private static void WriteGitSyncManifest(string path, IReadOnlyList<GitSyncManifestEntry> entries)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payloadEntries = new List<object>();
        foreach (var entry in entries)
        {
            payloadEntries.Add(new
            {
                repoInput = entry.RepoInput,
                repoBaseUrl = entry.RepoBaseUrl,
                repo = entry.Repo,
                destination = entry.Destination,
                authType = entry.AuthType,
                requestedRef = entry.RequestedReference,
                resolvedRef = entry.ResolvedReference
            });
        }

        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            entries = payloadEntries
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private static string NormalizeGitAuthType(string? value, int? index)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "auto";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" or "default" => "auto",
            "token" or "pat" or "basic" => "token",
            "ssh" => "ssh",
            "none" or "anonymous" or "off" or "disabled" => "none",
            _ => throw new InvalidOperationException(index.HasValue
                ? $"git-sync repos[{index.Value}] has unsupported authType '{value}'. Supported values: auto, token, ssh, none."
                : $"git-sync has unsupported authType '{value}'. Supported values: auto, token, ssh, none.")
        };
    }

    private static string? ResolveGitAuthHeader(string authType, string? token, string? username, int? index)
    {
        if (authType.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            authType.Equals("ssh", StringComparison.OrdinalIgnoreCase))
            return null;

        if (authType.Equals("token", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(index.HasValue
                ? $"git-sync repos[{index.Value}] authType 'token' requires token/tokenEnv."
                : "git-sync authType 'token' requires token/tokenEnv.");
        }

        return BuildGitAuthHeader(token, username);
    }

    private static string? BuildGitAuthHeader(string? token, string? username)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var user = string.IsNullOrWhiteSpace(username) ? "x-access-token" : username.Trim();
        var raw = $"{user}:{token.Trim()}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static void ConfigureSparseCheckout(string destination, string[] patterns, string? authHeader, int timeoutSeconds, int retry, int retryDelayMs)
    {
        var init = RunGitCommandWithRetry(destination, new[] { "sparse-checkout", "init", "--cone" }, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (init.ExitCode != 0)
        {
            init = RunGitCommandWithRetry(destination, new[] { "sparse-checkout", "init" }, authHeader, timeoutSeconds, retry, retryDelayMs);
            if (init.ExitCode != 0)
            {
                var preview = FirstNonEmptyLine(init.Error, init.Output) ?? "unknown error";
                throw new InvalidOperationException($"git-sync sparse-checkout init failed: {preview}");
            }
        }

        var setArgs = new List<string> { "sparse-checkout", "set" };
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                setArgs.Add(pattern.Trim());
        }

        var set = RunGitCommandWithRetry(destination, setArgs, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (set.ExitCode != 0)
        {
            var preview = FirstNonEmptyLine(set.Error, set.Output) ?? "unknown error";
            throw new InvalidOperationException($"git-sync sparse-checkout set failed: {preview}");
        }
    }

    private static void CheckoutReference(string destination, string reference, string? authHeader, int timeoutSeconds, int retry, int retryDelayMs)
    {
        var checkout = RunGitCommandWithRetry(destination, new[] { "checkout", "--force", reference }, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (checkout.ExitCode == 0)
            return;

        var fetch = RunGitCommandWithRetry(destination, new[] { "fetch", "origin", reference }, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (fetch.ExitCode != 0)
        {
            var preview = FirstNonEmptyLine(fetch.Error, checkout.Error, fetch.Output, checkout.Output) ?? "unknown error";
            throw new InvalidOperationException($"git-sync checkout failed for ref '{reference}': {preview}");
        }

        var checkoutFetched = RunGitCommandWithRetry(destination, new[] { "checkout", "--force", "FETCH_HEAD" }, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (checkoutFetched.ExitCode != 0)
        {
            var preview = FirstNonEmptyLine(checkoutFetched.Error, checkoutFetched.Output) ?? "unknown error";
            throw new InvalidOperationException($"git-sync checkout FETCH_HEAD failed for ref '{reference}': {preview}");
        }
    }

    private static void TryFastForwardPull(string destination, string? authHeader, int timeoutSeconds, int retry, int retryDelayMs)
    {
        _ = RunGitCommandWithRetry(destination, new[] { "pull", "--ff-only" }, authHeader, timeoutSeconds, retry, retryDelayMs);
    }

    private static void UpdateSubmodules(string destination, bool recursive, int depth, string? authHeader, int timeoutSeconds, int retry, int retryDelayMs)
    {
        var updateArgs = new List<string> { "submodule", "update", "--init" };
        if (recursive)
            updateArgs.Add("--recursive");
        if (depth > 0)
        {
            updateArgs.Add("--depth");
            updateArgs.Add(depth.ToString());
        }

        var update = RunGitCommandWithRetry(destination, updateArgs, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (update.ExitCode == 0)
            return;

        var withProtocolArgs = new List<string> { "-c", "protocol.file.allow=always" };
        withProtocolArgs.AddRange(updateArgs);
        var updateWithProtocol = RunGitCommandWithRetry(destination, withProtocolArgs, authHeader, timeoutSeconds, retry, retryDelayMs);
        if (updateWithProtocol.ExitCode == 0)
            return;

        var preview = FirstNonEmptyLine(updateWithProtocol.Error, update.Error, updateWithProtocol.Output, update.Output) ?? "unknown error";
        throw new InvalidOperationException($"git-sync submodule update failed: {preview}");
    }

    private static string? ResolveHeadReference(string destination, string? authHeader, int timeoutSeconds, int retry, int retryDelayMs)
    {
        var branch = RunGitCommandWithRetry(destination, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, authHeader, timeoutSeconds, retry, retryDelayMs);
        var branchName = branch.Output?.Trim();
        if (branch.ExitCode == 0 && !string.IsNullOrWhiteSpace(branchName) && !string.Equals(branchName, "HEAD", StringComparison.OrdinalIgnoreCase))
            return branchName;

        var commit = RunGitCommandWithRetry(destination, new[] { "rev-parse", "--short", "HEAD" }, authHeader, timeoutSeconds, retry, retryDelayMs);
        return commit.ExitCode == 0 && !string.IsNullOrWhiteSpace(commit.Output)
            ? commit.Output.Trim()
            : null;
    }

    private static string? ResolveHeadCommit(string destination, string? authHeader, int timeoutSeconds, int retry, int retryDelayMs)
    {
        var commit = RunGitCommandWithRetry(destination, new[] { "rev-parse", "HEAD" }, authHeader, timeoutSeconds, retry, retryDelayMs);
        return commit.ExitCode == 0 && !string.IsNullOrWhiteSpace(commit.Output)
            ? commit.Output.Trim()
            : null;
    }

    private static (int ExitCode, string Output, string Error) RunGitCommandWithRetry(
        string workingDirectory,
        IReadOnlyList<string> args,
        string? authHeader,
        int timeoutSeconds,
        int retry,
        int retryDelayMs)
    {
        var attempts = Math.Max(1, retry + 1);
        (int ExitCode, string Output, string Error) last = (1, string.Empty, string.Empty);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            last = RunGitCommand(workingDirectory, args, authHeader, timeoutSeconds);
            if (last.ExitCode == 0 || attempt >= attempts)
                return last;

            if (retryDelayMs > 0)
                System.Threading.Thread.Sleep(retryDelayMs);
        }

        return last;
    }

    private static string NormalizeGitLockMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "off";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "off" or "none" or "disabled" => "off",
            "verify" or "check" => "verify",
            "update" or "write" => "update",
            _ => throw new InvalidOperationException($"git-sync has unsupported lockMode '{value}'. Supported values: off, verify, update.")
        };
    }

    private static string? ResolveGitSyncLockPath(JsonElement step, string baseDir, string lockMode)
    {
        var lockPath = GetString(step, "lockPath") ?? GetString(step, "lock-path") ?? GetString(step, "lock");
        if (string.IsNullOrWhiteSpace(lockPath))
        {
            if (lockMode == "off")
                return null;
            lockPath = Path.Combine(".powerforge", "git-sync-lock.json");
        }

        var resolved = ResolvePath(baseDir, lockPath);
        return string.IsNullOrWhiteSpace(resolved)
            ? null
            : Path.GetFullPath(resolved);
    }

    private static Dictionary<string, GitSyncLockEntry> LoadGitSyncLock(string lockPath)
    {
        if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
            throw new InvalidOperationException($"git-sync verify mode requires lock file: {lockPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"git-sync lock file is invalid: {lockPath}");

        var result = new Dictionary<string, GitSyncLockEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            var destination = entry.TryGetProperty("destination", out var destElement) && destElement.ValueKind == JsonValueKind.String
                ? destElement.GetString()
                : null;
            var commit = entry.TryGetProperty("commit", out var commitElement) && commitElement.ValueKind == JsonValueKind.String
                ? commitElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(commit))
                continue;

            var key = Path.GetFullPath(destination);
            result[key] = new GitSyncLockEntry
            {
                RepoInput = entry.TryGetProperty("repoInput", out var repoInputElement) && repoInputElement.ValueKind == JsonValueKind.String
                    ? repoInputElement.GetString()
                    : null,
                Repo = entry.TryGetProperty("repo", out var repoElement) && repoElement.ValueKind == JsonValueKind.String
                    ? repoElement.GetString()
                    : null,
                Destination = key,
                Commit = commit.Trim()
            };
        }

        return result;
    }

    private static string ResolveLockedCommit(IReadOnlyDictionary<string, GitSyncLockEntry> lockEntries, GitSyncRequest request, int index)
    {
        var key = Path.GetFullPath(request.DestinationFull);
        if (!lockEntries.TryGetValue(key, out var entry) || string.IsNullOrWhiteSpace(entry.Commit))
            throw new InvalidOperationException($"git-sync verify mode: repos[{index}] is missing lock entry for destination '{request.DestinationFull}'.");
        return entry.Commit;
    }

    private static void WriteGitSyncLock(string lockPath, IReadOnlyList<GitSyncLockEntry> entries)
    {
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payloadEntries = new List<object>();
        foreach (var entry in entries)
        {
            payloadEntries.Add(new
            {
                repoInput = entry.RepoInput,
                repo = entry.Repo,
                destination = entry.Destination,
                commit = entry.Commit
            });
        }

        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            entries = payloadEntries
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(lockPath, json);
    }

    private static GitSyncRequest WithReference(GitSyncRequest source, string reference)
    {
        return new GitSyncRequest
        {
            RepoInput = source.RepoInput,
            RepoBaseUrl = source.RepoBaseUrl,
            Repo = source.Repo,
            DestinationFull = source.DestinationFull,
            Reference = reference,
            Clean = source.Clean,
            FetchTags = source.FetchTags,
            Depth = source.Depth,
            TimeoutSeconds = source.TimeoutSeconds,
            Retry = source.Retry,
            RetryDelayMs = source.RetryDelayMs,
            AuthType = source.AuthType,
            AuthHeader = source.AuthHeader,
            SparseCheckout = source.SparseCheckout,
            Submodules = source.Submodules,
            SubmodulesRecursive = source.SubmodulesRecursive,
            SubmoduleDepth = source.SubmoduleDepth,
            BaseDirectory = source.BaseDirectory
        };
    }

    private static (int ExitCode, string Output, string Error) RunGitCommand(
        string workingDirectory,
        IReadOnlyList<string> args,
        string? authHeader,
        int timeoutSeconds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"http.extraHeader=AUTHORIZATION: basic {authHeader}");
        }

        foreach (var arg in args)
        {
            if (!string.IsNullOrWhiteSpace(arg))
                startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"git-sync failed to start git: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var finished = process.WaitForExit(Math.Max(1, timeoutSeconds) * 1000);
        if (!finished)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best-effort cleanup
            }

            throw new InvalidOperationException($"git-sync timed out after {timeoutSeconds}s.");
        }

        return (process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
