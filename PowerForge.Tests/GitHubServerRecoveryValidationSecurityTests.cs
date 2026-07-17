using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubServerRecoveryValidationSecurityTests
{
    private const string CaptureUser = "powerforge-example-backup";
    private const string CallerRepository = "EvotecIT/ExampleSite";
    private const string EngineRepository = "EvotecIT/PSPublishModule";
    private const string EngineRef = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Recipient = "age1example";
    private const string ExpectedPlainCaptureCommand = "/usr/bin/tar -czf - /etc/example/config";
    private const string ExpectedCaptureCommand = "/usr/local/sbin/powerforge-server-encrypted-capture --recipient age1example -- /etc/example/secret";
    private const string ExpectedInspectCommand = "/usr/sbin/apachectl -S";

    [Theory]
    [InlineData("https://github.com/EvotecIT/ExampleSite.git")]
    [InlineData("ssh://git@github.com/EvotecIT/ExampleSite.git")]
    [InlineData("git@github.com:EvotecIT/ExampleSite.git")]
    [InlineData("git@github.com-example:EvotecIT/ExampleSite.git")]
    public void Validator_ShouldAcceptExplicitGitHubRepositoryForms(string repositoryUrl)
    {
        var result = RunValidator(repositoryUrl: repositoryUrl);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData("https://evilgithub.com/EvotecIT/ExampleSite.git")]
    [InlineData("git@github.com-evil.com:EvotecIT/ExampleSite.git")]
    public void Validator_ShouldRejectLookalikeGitHubHosts(string repositoryUrl)
    {
        var result = RunValidator(repositoryUrl: repositoryUrl);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a supported GitHub URL", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectBroaderEncryptedCaptureAliases()
    {
        var sudoers = $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}, /usr/bin/tar -czf - /etc/example/secret\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(
            result.AllOutput.Contains("must authorize exactly one command", StringComparison.Ordinal),
            result.AllOutput);
    }

    [Fact]
    public void Validator_ShouldIgnoreBroaderAliasesNotGrantedToCaptureUser()
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      $"Cmnd_Alias DEPLOY_BROAD = {ExpectedCaptureCommand}, /usr/bin/tar -czf - /etc/example/secret\n" +
                      "deployment-user ALL=(root) NOPASSWD: DEPLOY_BROAD\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectCallerControlledEncryptionHelpers()
    {
        var result = RunValidator(helperFromCaller: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact managed helper from the pinned PowerForge engine", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectRepositoryPathsThatShadowTheEngineHelper()
    {
        var result = RunValidator(shadowEngineHelper: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact managed helper from the pinned PowerForge engine", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other-user", "root")]
    [InlineData(CaptureUser, "backup")]
    public void Validator_ShouldBindAuthorizationToCaptureUserAndRoot(string principal, string runAs)
    {
        var sudoers = BuildExpectedSudoers(principal, runAs);

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            principal == CaptureUser ? "only as root" : "do not authorize the exact hardened encrypted-capture command",
            result.AllOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectAdditionalCaptureAccountGrants()
    {
        var sudoers = $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_INSPECT = {ExpectedInspectCommand}\n" +
                      "Cmnd_Alias BACKUP_DIRECT_TAR = /usr/bin/tar -czf - /etc/example/secret\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT, BACKUP_DIRECT_TAR\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectAlternateHostCaptureAccountGrants()
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      $"{CaptureUser} localhost=(root) NOPASSWD: BACKUP_ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must use the exact ALL=(root) form", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectInvalidSudoersAliasNames()
    {
        var sudoers = $"Cmnd_Alias BACKUP-ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP-ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("invalid command alias", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldIgnoreCloneOnlyNonGitHubRepositories()
    {
        var result = RunValidator(includeCloneOnlyRepository: true);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectSymlinkModeInPinnedSources()
    {
        var result = RunValidator(pinSudoersAsSymlink: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unsupported Git mode 120000", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectWildcardedPlainCapturePaths()
    {
        var result = RunValidator(plainCaptureTarget: "/etc/example/*.conf");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Plain recovery capture path contains unsupported characters", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireRootOwnedHelperMetadata()
    {
        var result = RunValidator(helperOwner: CaptureUser);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact managed helper from the pinned PowerForge engine", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireRootOwnedSudoersMetadata()
    {
        var result = RunValidator(sudoersOwner: CaptureUser);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must be root-owned files with mode 440 or 0440", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("440")]
    [InlineData("0440")]
    public void Validator_ShouldAcceptSchemaSupportedSudoersModes(string mode)
    {
        var result = RunValidator(sudoersMode: mode);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectSchemaInvalidSudoersModes()
    {
        var result = RunValidator(sudoersMode: "400");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must be root-owned files with mode 440 or 0440", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectUntaggedSudoersTargets()
    {
        var result = RunValidator(tagSudoers: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must declare sudoers validation", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/sudoers", "must not replace /etc/sudoers")]
    [InlineData("/etc/sudoers.d", "must not replace /etc/sudoers")]
    [InlineData("/etc/sudoers.d/powerforge-apache", "must declare sudoers validation")]
    public void Validator_ShouldRejectSudoersTargetsFromAlternateManifestSections(string target, string expectedError)
    {
        var result = RunValidator(alternateManagedSudoersTarget: target);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedError, result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#1001 ALL=(root) NOPASSWD: /bin/sh")]
    [InlineData("#1001,other-user ALL=(root) NOPASSWD: /bin/sh")]
    public void Validator_ShouldRejectNumericUidPrincipals(string numericGrant)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      numericGrant + "\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not use numeric user principals", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldInspectEveryManagedSudoersSource()
    {
        var extraSudoers = "Cmnd_Alias BACKUP_DIRECT = /usr/bin/tar -czf - /etc/example/secret\n" +
                           $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_DIRECT\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldAllowUnrelatedMultiCommandAliases()
    {
        var extraSudoers = "Cmnd_Alias DEPLOY_TOOLS = /usr/bin/true, /usr/bin/false\n" +
                           "deployment-user ALL=(root) NOPASSWD: DEPLOY_TOOLS\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldResolveCaptureAliasesAcrossManagedSudoersSources()
    {
        var grant = $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT\n";

        var result = RunValidator(sudoers: BuildExpectedAliases(), additionalSudoers: grant);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldTreatMissingCaptureConfigurationAsEmpty()
    {
        var result = RunValidator(includeCapture: false);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectUserAliasesInManagedSudoers()
    {
        var extraSudoers = $"User_Alias PF_BACKUP = {CaptureUser}\n" +
                           "Cmnd_Alias BACKUP_DIRECT = /bin/sh\n" +
                           "PF_BACKUP ALL=(root) NOPASSWD: BACKUP_DIRECT\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not use User_Alias entries", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldCompareGrantedCommandsCaseSensitively()
    {
        var sudoers = $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand.Replace("/usr/bin/tar", "/USR/BIN/TAR", StringComparison.Ordinal)}\n" +
                      $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_INSPECT = {ExpectedInspectCommand}\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    private static ValidationResult RunValidator(
        string repositoryUrl = "https://github.com/EvotecIT/ExampleSite.git",
        string? sudoers = null,
        bool helperFromCaller = false,
        bool shadowEngineHelper = false,
        bool includeCloneOnlyRepository = false,
        bool pinSudoersAsSymlink = false,
        string plainCaptureTarget = "/etc/example/config",
        string helperOwner = "root",
        string sudoersOwner = "root",
        string sudoersMode = "440",
        bool tagSudoers = true,
        string? alternateManagedSudoersTarget = null,
        string? additionalSudoers = null,
        bool includeCapture = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-recovery-source-security-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "caller");
        var engineRoot = Path.Combine(root, "engine");
        Directory.CreateDirectory(Path.Combine(workspace, "deploy", "linux"));
        Directory.CreateDirectory(Path.Combine(engineRoot, "Deployment", "Linux"));

        try
        {
            File.WriteAllText(
                Path.Combine(engineRoot, "Deployment", "Linux", "powerforge-server-encrypted-capture.sh"),
                "#!/usr/bin/env bash\nset -euo pipefail\n");
            File.WriteAllText(
                Path.Combine(workspace, "deploy", "linux", "backup.sudoers"),
                sudoers ?? BuildExpectedSudoers(CaptureUser, "root"));
            if (additionalSudoers is not null)
                File.WriteAllText(Path.Combine(workspace, "deploy", "linux", "extra.sudoers"), additionalSudoers);
            if (alternateManagedSudoersTarget is not null)
                File.WriteAllText(Path.Combine(workspace, "deploy", "linux", "alternate.sudoers"), "# managed source fixture\n");

            var helperSource = "/srv/engine/Deployment/Linux/powerforge-server-encrypted-capture.sh";
            if (helperFromCaller)
            {
                File.WriteAllText(Path.Combine(workspace, "deploy", "linux", "fake-helper.sh"), "#!/usr/bin/env bash\nexit 0\n");
                helperSource = "/srv/caller/deploy/linux/fake-helper.sh";
            }
            if (shadowEngineHelper)
            {
                Directory.CreateDirectory(Path.Combine(workspace, "Linux"));
                File.WriteAllText(
                    Path.Combine(workspace, "Linux", "powerforge-server-encrypted-capture.sh"),
                    "#!/usr/bin/env bash\nexit 0\n");
            }

            InitializeGitRepository(workspace);
            if (pinSudoersAsSymlink)
            {
                var sudoersPath = Path.Combine(workspace, "deploy", "linux", "backup.sudoers");
                var blob = RunProcess("git", workspace, "hash-object", sudoersPath).StandardOutput.Trim();
                RunProcess(
                    "git",
                    workspace,
                    "update-index",
                    "--cacheinfo",
                    $"120000,{blob},deploy/linux/backup.sudoers").EnsureSuccess();
                RunProcess("git", workspace, "commit", "-m", "Pin symlink mode", "--quiet").EnsureSuccess();
            }
            var callerRef = RunProcess("git", workspace, "rev-parse", "HEAD").StandardOutput.Trim();
            var manifestPath = Path.Combine(root, "manifest.json");
            var repositories = new List<object>
            {
                new { url = repositoryUrl, path = "/srv/caller", @ref = callerRef },
                new { url = "https://github.com/EvotecIT/PSPublishModule.git", path = "/srv/engine", @ref = EngineRef }
            };
            if (shadowEngineHelper)
                repositories.Add(new { url = repositoryUrl, path = "/srv/engine/Deployment", @ref = callerRef });
            if (includeCloneOnlyRepository)
                repositories.Add(new { url = "git@example.test:private/application.git", path = "/srv/clone-only", @ref = callerRef });

            var managedPaths = new List<object>
            {
                new
                {
                    path = "/usr/local/sbin/powerforge-server-encrypted-capture",
                    source = helperSource,
                    kind = "file",
                    owner = helperOwner,
                    group = "root",
                    mode = "755"
                },
                new
                {
                    path = "/etc/sudoers.d/powerforge-example-backup",
                    source = "/srv/caller/deploy/linux/backup.sudoers",
                    kind = "file",
                    owner = sudoersOwner,
                    group = "root",
                    mode = sudoersMode,
                    validation = tagSudoers ? "sudoers" : null
                }
            };
            if (additionalSudoers is not null)
            {
                managedPaths.Add(new
                {
                    path = "/etc/sudoers.d/powerforge-example-extra",
                    source = "/srv/caller/deploy/linux/extra.sudoers",
                    kind = "file",
                    owner = "root",
                    group = "root",
                    mode = "440",
                    validation = "sudoers"
                });
            }

            object? capture = includeCapture
                ? new
                {
                    plainFiles = new[] { new { target = plainCaptureTarget, required = true } },
                    encryptedFiles = new[] { new { target = "/etc/example/secret", required = true } },
                    commands = new[] { new { id = "apache-vhosts", command = "sudo -n apachectl -S", required = true } }
                }
                : null;
            object? backupTarget = includeCapture ? new { recipient = Recipient } : null;
            object? apache = alternateManagedSudoersTarget is not null
                ? new
                {
                    sites = new[]
                    {
                        new
                        {
                            source = "/srv/caller/deploy/linux/alternate.sudoers",
                            target = alternateManagedSudoersTarget
                        }
                    }
                }
                : null;
            var manifest = new
            {
                repositories,
                paths = managedPaths,
                apache,
                capture,
                backupTarget
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

            var wrapperPath = Path.Combine(root, "invoke-validator.ps1");
            File.WriteAllText(wrapperPath, """
                param(
                    [Parameter(Mandatory)][string] $ValidatorPath,
                    [Parameter(Mandatory)][string] $ManifestPath,
                    [Parameter(Mandatory)][string] $Workspace,
                    [Parameter(Mandatory)][string] $EngineRoot,
                    [Parameter(Mandatory)][string] $EngineRef,
                    [Parameter(Mandatory)][string] $CallerRepository,
                    [Parameter(Mandatory)][string] $EngineRepository,
                    [Parameter(Mandatory)][string] $CaptureUser
                )
                $ErrorActionPreference = 'Stop'
                $env:POWERFORGE_ENGINE_REF = $EngineRef
                $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 100
                & $ValidatorPath `
                    -Manifest $manifest `
                    -Workspace $Workspace `
                    -EngineRoot $EngineRoot `
                    -CallerRepository $CallerRepository `
                    -EngineRepository $EngineRepository `
                    -CaptureUser $CaptureUser
                """);

            var validatorPath = GetRepoPath(
                ".github", "actions", "powerforge-server-recovery-validate", "Assert-PowerForgeServerRecoverySources.ps1");
            return RunProcess(
                "pwsh",
                root,
                "-NoLogo", "-NoProfile", "-File", wrapperPath,
                "-ValidatorPath", validatorPath,
                "-ManifestPath", manifestPath,
                "-Workspace", workspace,
                "-EngineRoot", engineRoot,
                "-EngineRef", EngineRef,
                "-CallerRepository", CallerRepository,
                "-EngineRepository", EngineRepository,
                "-CaptureUser", CaptureUser);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort test cleanup */ }
        }
    }

    private static string BuildExpectedAliases()
        => $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand}\n" +
           $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
           $"Cmnd_Alias BACKUP_INSPECT = {ExpectedInspectCommand}\n";

    private static string BuildExpectedSudoers(string principal, string runAs)
        => BuildExpectedAliases() +
           $"{principal} ALL=({runAs}) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT\n";

    private static void InitializeGitRepository(string path)
    {
        RunProcess("git", path, "init", "-b", "main").EnsureSuccess();
        RunProcess("git", path, "config", "user.name", "PowerForge Tests").EnsureSuccess();
        RunProcess("git", path, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
        RunProcess("git", path, "config", "commit.gpgsign", "false").EnsureSuccess();
        RunProcess("git", path, "add", ".").EnsureSuccess();
        RunProcess("git", path, "commit", "-m", "Recovery fixture", "--quiet").EnsureSuccess();
    }

    private static ValidationResult RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ValidationResult(process.ExitCode, stdout, stderr);
    }

    private static string GetRepoPath(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return Path.Combine([current.FullName, .. relativePath]);
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private readonly record struct ValidationResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + StandardError;

        public void EnsureSuccess()
        {
            if (ExitCode != 0)
                throw new InvalidOperationException(AllOutput);
        }
    }
}
