using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    private static ValidationResult RunValidator(
        string repositoryUrl = "https://github.com/EvotecIT/ExampleSite.git",
        string? sudoers = null,
        bool helperFromCaller = false,
        bool shadowEngineHelper = false,
        bool includeCloneOnlyRepository = false,
        bool includeCloneOnlyRepositoryWithoutUrl = false,
        bool pinSudoersAsSymlink = false,
        string plainCaptureTarget = "/etc/example/config",
        string helperOwner = "root",
        string helperMode = "755",
        string sudoersOwner = "root",
        string sudoersMode = "440",
        bool tagSudoers = true,
        string? alternateManagedSudoersTarget = null,
        string? additionalSudoers = null,
        bool includeCapture = true,
        string recipient = Recipient,
        string? recipientEnv = null,
        string captureCommand = "sudo -n apachectl -S",
        string? visudoPathOverride = null,
        string callerRepository = CallerRepository,
        string engineRepository = EngineRepository,
        bool includeCaptureAccount = true,
        string authorizedKeyContent = RestrictedCaptureKey,
        string authorizedKeyOwner = "root",
        string captureDirectoryOwner = "root",
        bool includeOptionalEncryptedCapture = false)
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
            var expectedEncryptedCommand = includeOptionalEncryptedCapture
                ? ExpectedCaptureCommand + " --optional /var/lib/example/optional"
                : ExpectedCaptureCommand;
            File.WriteAllText(
                Path.Combine(workspace, "deploy", "linux", "backup.sudoers"),
                sudoers ?? (includeCapture
                    ? BuildExpectedSudoers(CaptureUser, "root", expectedEncryptedCommand)
                    : "# no privileged capture grants\n"));
            File.WriteAllText(
                Path.Combine(workspace, "deploy", "linux", "backup-authorized_keys"),
                authorizedKeyContent + "\n");
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
            if (includeCloneOnlyRepositoryWithoutUrl)
                repositories.Add(new { role = "application", path = "/srv/manual-clone", @ref = callerRef });

            var managedPaths = new List<object>
            {
                new
                {
                    path = "/usr/local/sbin/powerforge-server-encrypted-capture",
                    source = helperSource,
                    kind = "file",
                    owner = helperOwner,
                    group = "root",
                    mode = helperMode
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
            if (includeCapture)
            {
                managedPaths.Add(new
                {
                    path = $"/var/lib/{CaptureUser}",
                    kind = "directory",
                    owner = captureDirectoryOwner,
                    group = captureDirectoryOwner,
                    mode = "755"
                });
                managedPaths.Add(new
                {
                    path = $"/var/lib/{CaptureUser}/.ssh",
                    kind = "directory",
                    owner = captureDirectoryOwner,
                    group = captureDirectoryOwner,
                    mode = "755"
                });
                managedPaths.Add(new
                {
                    path = $"/var/lib/{CaptureUser}/.ssh/authorized_keys",
                    source = "/srv/caller/deploy/linux/backup-authorized_keys",
                    kind = "file",
                    owner = authorizedKeyOwner,
                    group = authorizedKeyOwner,
                    mode = "600"
                });
            }
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

            var encryptedFiles = includeOptionalEncryptedCapture
                ? new[]
                {
                    new { target = "/etc/example/secret", required = true },
                    new { target = "/var/lib/example/optional", required = false }
                }
                : new[] { new { target = "/etc/example/secret", required = true } };
            object? capture = includeCapture
                ? new
                {
                    plainFiles = new[] { new { target = plainCaptureTarget, required = true } },
                    encryptedFiles,
                    commands = new[] { new { id = "apache-vhosts", command = captureCommand, required = true } }
                }
                : null;
            object? backupTarget = includeCapture
                ? recipientEnv is null
                    ? new { recipient }
                    : new { recipientEnv }
                : null;
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
                accounts = includeCapture && includeCaptureAccount
                    ? new[] { new { name = CaptureUser, home = $"/var/lib/{CaptureUser}" } }
                    : null,
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
                    [Parameter(Mandatory)][string] $CaptureUser,
                    [Parameter(Mandatory)][string] $VisudoPath
                )
                $ErrorActionPreference = 'Stop'
                $env:POWERFORGE_ENGINE_REF = $EngineRef
                $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 100
                try {
                    & $ValidatorPath `
                        -Manifest $manifest `
                        -Workspace $Workspace `
                        -EngineRoot $EngineRoot `
                        -CallerRepository $CallerRepository `
                        -EngineRepository $EngineRepository `
                        -CaptureUser $CaptureUser `
                        -VisudoPath $VisudoPath
                } catch {
                    [Console]::Error.WriteLine($_.Exception.Message)
                    exit 1
                }
                """);

            var validatorPath = GetRepoPath(
                ".github", "actions", "powerforge-server-recovery-validate", "Assert-PowerForgeServerRecoverySources.ps1");
            var visudoLogPath = Path.Combine(root, "visudo-invocations.log");
            var visudoPath = visudoPathOverride ?? CreateVisudoStub(root, visudoLogPath);
            var result = RunProcess(
                "pwsh",
                root,
                "-NoLogo", "-NoProfile", "-File", wrapperPath,
                "-ValidatorPath", validatorPath,
                "-ManifestPath", manifestPath,
                "-Workspace", workspace,
                "-EngineRoot", engineRoot,
                "-EngineRef", EngineRef,
                "-CallerRepository", callerRepository,
                "-EngineRepository", engineRepository,
                "-CaptureUser", CaptureUser,
                "-VisudoPath", visudoPath);
            return result with
            {
                VisudoInvocations = File.Exists(visudoLogPath)
                    ? File.ReadAllLines(visudoLogPath)
                    : []
            };
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort test cleanup */ }
        }
    }

    private static string BuildExpectedAliases(string encryptedCommand = ExpectedCaptureCommand)
        => $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand}\n" +
           $"Cmnd_Alias BACKUP_ENCRYPTED = {encryptedCommand}\n" +
           $"Cmnd_Alias BACKUP_INSPECT = {ExpectedInspectCommand}\n";

    private static string BuildExpectedSudoers(
        string principal,
        string runAs,
        string encryptedCommand = ExpectedCaptureCommand)
        => BuildExpectedAliases(encryptedCommand) +
           $"{principal} ALL=({runAs}) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT\n";

    private static string CreateVisudoStub(string root, string logPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "visudo.cmd");
            File.WriteAllText(path, $"""
                @echo off
                setlocal
                if not "%~1"=="-c" exit /b 91
                if not "%~2"=="-f" exit /b 92
                if "%~3"=="" exit /b 93
                if not "%~4"=="" exit /b 94
                if not exist "%~3" exit /b 95
                echo %~3>>"{logPath}"
                findstr /C:"this is not valid sudoers" "%~3" >nul
                if not errorlevel 1 (
                  echo VISUDO_STDOUT_SENTINEL
                  echo VISUDO_STDERR_SENTINEL 1>&2
                  exit /b 1
                )
                exit /b 0
                """);
            return path;
        }

        var stub = Path.Combine(root, "visudo-stub.sh");
        File.WriteAllText(stub, $$"""
            #!/usr/bin/env sh
            set -eu
            [ "$#" -eq 3 ] || exit 91
            [ "$1" = '-c' ] || exit 92
            [ "$2" = '-f' ] || exit 93
            [ -f "$3" ] || exit 94
            printf '%s\n' "$3" >> '{{logPath}}'
            if grep -Fq 'this is not valid sudoers' "$3"; then
              echo 'VISUDO_STDOUT_SENTINEL'
              echo 'VISUDO_STDERR_SENTINEL' >&2
              exit 1
            fi
            exit 0
            """);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return stub;
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
        public string[] VisudoInvocations { get; init; } = [];

        public string AllOutput => StandardOutput + StandardError;

        public void EnsureSuccess()
        {
            if (ExitCode != 0)
                throw new InvalidOperationException(AllOutput);
        }
    }
}
