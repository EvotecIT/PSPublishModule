using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerScaffold(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var options = ParseServerScaffoldOptions(subArgs);
        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var files = BuildServerScaffoldFiles(options);
        var resolvedFiles = files.Keys.ToDictionary(
            static path => path,
            path => ResolveScaffoldPath(outputRoot, path),
            StringComparer.Ordinal);

        if (!options.Force)
        {
            var existing = resolvedFiles.Where(static entry => File.Exists(entry.Value)).Select(static entry => entry.Key).ToArray();
            if (existing.Length > 0)
                throw new InvalidOperationException("Scaffold would overwrite existing files: " + string.Join(", ", existing) + ". Pass --force only after reviewing those files.");
        }

        foreach (var file in files)
        {
            var resolvedPath = resolvedFiles[file.Key];
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
            File.WriteAllText(resolvedPath, NormalizeGeneratedText(file.Value), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var nextSteps = new[]
        {
            "Review deploy/linux/ONBOARDING.md and replace only the public-key example files.",
            "Create and branch-restrict the production GitHub environment before storing secrets.",
            "Run server plan, bootstrap-plan, inspect, and verify before the first protected deployment.",
            options.CloudflareEnabled
                ? "Provision a per-site Cloudflare token and zone id before enabling the generated workflow."
                : "Cloudflare remains disabled; add --cloudflare only after per-site credentials are ready."
        };
        var result = new PowerForgeServerScaffoldResult
        {
            OutputRoot = outputRoot,
            Domain = options.Domain,
            SiteId = options.SiteId,
            CloudflareEnabled = options.CloudflareEnabled,
            PrivateRepository = options.PrivateRepository,
            Files = files.Keys.Order(StringComparer.Ordinal).ToArray(),
            NextSteps = nextSteps
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.scaffold",
                Success = true,
                ExitCode = 0,
                Config = "web.serverrecovery",
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.PowerForgeServerScaffoldResult)
            });
            return 0;
        }

        logger.Success($"Generated a thin PowerForge Linux website scaffold for {options.Domain}.");
        logger.Info($"Output: {outputRoot}");
        logger.Info($"Files: {files.Count}; Cloudflare: {(options.CloudflareEnabled ? "wired but not provisioned" : "disabled")}");
        foreach (var nextStep in nextSteps)
            logger.Info("- " + nextStep);
        return 0;
    }

    internal static IReadOnlyDictionary<string, string> BuildServerScaffoldFiles(PowerForgeServerScaffoldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var manifest = BuildServerScaffoldManifest(options);
        var manifestErrors = ValidateServerRecoveryManifest(manifest);
        if (manifestErrors.Length > 0)
            throw new InvalidOperationException("Generated recovery manifest is invalid: " + string.Join(" ", manifestErrors));

        var serializerOptions = new JsonSerializerOptions(WebCliJson.Options) { WriteIndented = true };
        var serializerContext = new PowerForgeWebCliJsonContext(serializerOptions);
        var manifestJson = JsonSerializer.Serialize(manifest, serializerContext.PowerForgeServerRecoveryManifest);
        var deployRoot = "deploy/linux";

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".github/workflows/website-deploy.yml"] = BuildScaffoldWebsiteWorkflow(options),
            [".github/workflows/server-backup.yml"] = BuildScaffoldBackupWorkflow(options),
            [$"{deployRoot}/{options.Domain}.env"] = BuildScaffoldSiteEnvironment(options),
            [$"{deployRoot}/{options.SiteId}.serverrecovery.json"] = manifestJson,
            [$"{deployRoot}/powerforge-{options.SiteId}.sudoers"] = BuildScaffoldDeploymentSudoers(options),
            [$"{deployRoot}/powerforge-{options.SiteId}-backup.sudoers"] = BuildScaffoldBackupSudoers(options, manifest),
            [$"{deployRoot}/powerforge-{options.SiteId}-authorized_keys.example"] = $"restrict ssh-ed25519 REPLACE_WITH_DEPLOYMENT_PUBLIC_KEY powerforge-{options.SiteId}-deploy\n",
            [$"{deployRoot}/powerforge-{options.SiteId}-backup-authorized_keys.example"] = $"restrict ssh-ed25519 REPLACE_WITH_BACKUP_PUBLIC_KEY powerforge-{options.SiteId}-backup\n",
            [$"{deployRoot}/ONBOARDING.md"] = BuildScaffoldOnboarding(options),
            [$"{options.WebsiteRoot}/deploy/apache.conf"] = BuildScaffoldApacheHttp(options),
            [$"{options.WebsiteRoot}/deploy/apache-ssl.conf"] = BuildScaffoldApacheHttps(options)
        };

        if (options.PrivateRepository)
        {
            files[$"{deployRoot}/github_known_hosts.example"] = "# Replace with reviewed GitHub SSH host-key entries; never populate this with deploy-time ssh-keyscan output.\n";
        }

        return files;
    }

    internal static PowerForgeServerScaffoldOptions ParseServerScaffoldOptions(string[] subArgs)
    {
        var domain = RequireScaffoldOption(subArgs, "--domain").Trim().ToLowerInvariant();
        var repository = RequireScaffoldOption(subArgs, "--repository").Trim();
        var repositoryRef = RequireScaffoldOption(subArgs, "--repository-ref").Trim().ToLowerInvariant();
        var engineRef = RequireScaffoldOption(subArgs, "--engine-ref").Trim().ToLowerInvariant();
        var host = RequireScaffoldOption(subArgs, "--host").Trim();
        var backupRepository = RequireScaffoldOption(subArgs, "--backup-repository").Trim();
        var backupRecipient = RequireScaffoldOption(subArgs, "--backup-recipient").Trim();
        var branch = (TryGetOptionValue(subArgs, "--branch") ?? "main").Trim();
        var websiteRoot = (TryGetOptionValue(subArgs, "--website-root") ?? "Website").Trim().Trim('/', '\\');
        var siteId = (TryGetOptionValue(subArgs, "--site-id") ?? BuildScaffoldSiteId(domain)).Trim().ToLowerInvariant();
        var smokePaths = (TryGetOptionValue(subArgs, "--smoke-paths") ?? "/ /sitemap.xml").Trim();
        var outputRoot = TryGetOptionValue(subArgs, "--out") ?? TryGetOptionValue(subArgs, "--output-dir") ?? Directory.GetCurrentDirectory();
        var portText = TryGetOptionValue(subArgs, "--ssh-port") ?? "22";
        var includeWwwAlias = HasOption(subArgs, "--www");

        if (!Regex.IsMatch(domain, "^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--domain must be a lowercase DNS domain name.");
        if (!Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--repository must be an owner/repository name.");
        if (!Regex.IsMatch(repositoryRef, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--repository-ref must be an exact 40-character commit.");
        if (!Regex.IsMatch(backupRepository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--backup-repository must be an owner/repository name.");
        if (!Regex.IsMatch(engineRef, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--engine-ref must be an exact 40-character commit.");
        if (!Regex.IsMatch(host, "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--host must be a DNS name or IPv4 address.");
        if (!Regex.IsMatch(branch, "^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant) || branch.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("--branch contains unsupported characters.");
        if (!Regex.IsMatch(siteId, "^[a-z][a-z0-9-]{0,13}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--site-id must be 1-14 lowercase letters, digits, or hyphens and start with a letter.");
        if (!Regex.IsMatch(websiteRoot, "^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant) || websiteRoot.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("--website-root must be a safe repository-relative path.");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("--ssh-port must be from 1 through 65535.");
        if (!backupRecipient.StartsWith("age1", StringComparison.Ordinal) || backupRecipient.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("--backup-recipient must be an age public recipient beginning with age1.");
        if (includeWwwAlias && domain.StartsWith("www.", StringComparison.Ordinal))
            throw new InvalidOperationException("--www cannot be combined with a domain that already starts with www.");
        var smokePathValues = smokePaths.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (smokePathValues.Length == 0 || smokePathValues.Any(static path =>
                !Regex.IsMatch(path, "^/[A-Za-z0-9._~!&'()*+,;=:@%/?-]*$", RegexOptions.CultureInvariant)))
        {
            throw new InvalidOperationException(
                "--smoke-paths must contain space-separated absolute URL paths using URL-safe characters; quotes, backslashes, control characters, dollar signs, and backticks are not supported.");
        }

        return new PowerForgeServerScaffoldOptions
        {
            Domain = domain,
            Repository = repository,
            RepositoryRef = repositoryRef,
            EngineRef = engineRef,
            Host = host,
            BackupRepository = backupRepository,
            BackupRecipient = backupRecipient,
            Branch = branch,
            WebsiteRoot = websiteRoot.Replace('\\', '/'),
            SiteId = siteId,
            SmokePaths = string.Join(' ', smokePathValues),
            SshPort = port,
            OutputRoot = outputRoot,
            PrivateRepository = HasOption(subArgs, "--private-repository"),
            CloudflareEnabled = HasOption(subArgs, "--cloudflare"),
            IncludeWwwAlias = includeWwwAlias,
            Force = HasOption(subArgs, "--force")
        };
    }

    private static string RequireScaffoldOption(string[] args, string name)
        => TryGetOptionValue(args, name) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing required option {name}.");

    private static string BuildScaffoldSiteId(string domain)
    {
        var firstLabel = domain.Split('.', 2)[0];
        var readable = Regex.Replace(firstLabel, "[^a-z0-9-]", string.Empty, RegexOptions.CultureInvariant).Trim('-');
        if (string.IsNullOrEmpty(readable))
            readable = "site";
        if (readable[0] is < 'a' or > 'z')
            readable = "s-" + readable;
        if (readable.Length > 9)
            readable = readable[..9].TrimEnd('-');

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(domain))).ToLowerInvariant();
        return readable + "-" + hash[..4];
    }

    private static string ResolveScaffoldPath(string outputRoot, string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolved.StartsWith(prefix, comparison))
            throw new InvalidOperationException($"Generated path escaped the scaffold output root: {relativePath}");
        return resolved;
    }

    private static string NormalizeGeneratedText(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
}

internal sealed class PowerForgeServerScaffoldOptions
{
    public string Domain { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string RepositoryRef { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string WebsiteRoot { get; set; } = "Website";
    public string EngineRef { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string BackupRepository { get; set; } = string.Empty;
    public string BackupRecipient { get; set; } = string.Empty;
    public string SmokePaths { get; set; } = "/ /sitemap.xml";
    public string OutputRoot { get; set; } = string.Empty;
    public bool PrivateRepository { get; set; }
    public bool CloudflareEnabled { get; set; }
    public bool IncludeWwwAlias { get; set; }
    public bool Force { get; set; }
}
