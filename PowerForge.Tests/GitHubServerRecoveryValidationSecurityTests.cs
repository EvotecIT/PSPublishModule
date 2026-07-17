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
    private const string ExpectedCaptureCommand = "/usr/local/sbin/powerforge-server-encrypted-capture --recipient age1example -- /etc/example/secret";

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

    [Fact]
    public void Validator_ShouldRejectLookalikeGitHubHosts()
    {
        var result = RunValidator(repositoryUrl: "https://evilgithub.com/EvotecIT/ExampleSite.git");

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
        Assert.Contains("must not authorize additional commands", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectBroaderAliasesAlongsideAnExactAlias()
    {
        var sudoers = $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_ENCRYPTED_BROAD = {ExpectedCaptureCommand}, /usr/bin/tar -czf - /etc/example/secret\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not authorize additional commands", result.AllOutput, StringComparison.Ordinal);
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
        var sudoers = $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"{principal} ALL=({runAs}) NOPASSWD: BACKUP_ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    private static ValidationResult RunValidator(
        string repositoryUrl = "https://github.com/EvotecIT/ExampleSite.git",
        string? sudoers = null,
        bool helperFromCaller = false,
        bool shadowEngineHelper = false)
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
                sudoers ?? $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n{CaptureUser} ALL=(root) NOPASSWD: BACKUP_ENCRYPTED\n");

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
            var callerRef = RunProcess("git", workspace, "rev-parse", "HEAD").StandardOutput.Trim();
            var manifestPath = Path.Combine(root, "manifest.json");
            var repositories = new List<object>
            {
                new { url = repositoryUrl, path = "/srv/caller", @ref = callerRef },
                new { url = "https://github.com/EvotecIT/PSPublishModule.git", path = "/srv/engine", @ref = EngineRef }
            };
            if (shadowEngineHelper)
                repositories.Add(new { url = repositoryUrl, path = "/srv/engine/Deployment", @ref = callerRef });

            var manifest = new
            {
                repositories,
                paths = new object[]
                {
                    new
                    {
                        path = "/usr/local/sbin/powerforge-server-encrypted-capture",
                        source = helperSource
                    },
                    new
                    {
                        path = "/etc/sudoers.d/powerforge-example-backup",
                        source = "/srv/caller/deploy/linux/backup.sudoers",
                        validation = "sudoers"
                    }
                },
                capture = new
                {
                    encryptedFiles = new[] { new { target = "/etc/example/secret", required = true } }
                },
                backupTarget = new { recipient = Recipient }
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
