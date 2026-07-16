using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoverySecurityTests
{
    [Fact]
    public void ManifestValidation_AcceptsSeparatedPlainAndEncryptedCaptureSets()
    {
        var manifest = CreateManifest();

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Empty(errors);
        Assert.Equal(["/etc/example/secret.env"], WebCliCommandHandlers.GetEncryptedRestorePaths(manifest));
    }

    [Fact]
    public void ManifestValidation_RejectsPlaintextSecretOverlap()
    {
        var manifest = CreateManifest();
        manifest.Capture!.PlainFiles =
        [
            new PowerForgeServerManagedFile { Target = "/etc/example", Required = true, Sensitive = false }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("overlaps encrypted capture path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("overlaps capture.plainFiles", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RequiresEncryptedSecretCoverageAndExactRestorePaths()
    {
        var manifest = CreateManifest();
        manifest.Capture!.EncryptedFiles =
        [
            new PowerForgeServerManagedFile { Target = "/etc/other/*", Required = true, Sensitive = true }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("not covered by capture.encryptedFiles", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must use an exact path", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsChildCaptureThatDoesNotCoverParentSecret()
    {
        var manifest = CreateManifest();
        manifest.Secrets![0].Path = "/etc/example";

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("not covered by capture.encryptedFiles", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsExcludedSecretInsideAnyCaptureArchive()
    {
        var manifest = CreateManifest();
        manifest.Secrets =
        [
            new PowerForgeServerSecret
            {
                Id = "excluded-plaintext-secret",
                Path = "/etc/example/private/token.txt",
                Capture = "exclude",
                RestoreMode = "file"
            },
            new PowerForgeServerSecret
            {
                Id = "excluded-encrypted-secret",
                Path = "/etc/example/secret.env",
                Capture = "exclude",
                RestoreMode = "file"
            }
        ];
        manifest.Capture!.PlainFiles =
        [
            new PowerForgeServerManagedFile { Target = "/etc/example/private", Required = true, Sensitive = false }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("could be published without encryption", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("would not be excluded", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsInvalidInheritedRestoreMetadata()
    {
        var manifest = CreateManifest();
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "secret",
                Path = "/etc/example/secret.env",
                Owner = "root;touch-pwned",
                Group = "root",
                Mode = "999"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("invalid owner", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid mode", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsAmbiguousManagedPathsAndDuplicateCaptures()
    {
        var manifest = CreateManifest();
        manifest.Paths =
        [
            new PowerForgeServerPath { Id = "secret", Path = "/etc/example/secret.env", Mode = "600" },
            new PowerForgeServerPath { Id = "secret", Path = "/etc/example/secret.env", Mode = "640" }
        ];
        manifest.Capture!.EncryptedFiles =
        [
            new PowerForgeServerManagedFile { Target = "/etc/example/secret.env", Required = true, Sensitive = true },
            new PowerForgeServerManagedFile { Target = "/etc/example/secret.env", Required = true, Sensitive = true }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("Managed path id", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Managed path '/etc/example/secret.env' is duplicated", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicates capture path", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsPathsThatCouldAlterGeneratedRestoreScript()
    {
        var manifest = CreateManifest();
        manifest.Capture!.EncryptedFiles![0].Target = "/etc/example/secret.env\nPOWERFORGE_ALLOWED_PATHS";

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("unsupported characters", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_DerivesPlainSensitivityButRequiresEncryptedConfirmation()
    {
        var manifest = CreateManifest();
        manifest.Capture!.PlainFiles![0].Sensitive = null;
        manifest.Capture.EncryptedFiles![0].Sensitive = null;

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.DoesNotContain(errors, error => error.Contains("capture.plainFiles[0]", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("capture.encryptedFiles[0] must explicitly set sensitive=true", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsAmbiguousOrUnimplementedRetention()
    {
        var manifest = CreateManifest();
        manifest.BackupTarget = new PowerForgeServerBackupTarget
        {
            Retention = new PowerForgeServerBackupRetention { KeepLatestInTree = 24, KeepLatest = 12, KeepDays = 30 }
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("keepDays is not implemented", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must not set both", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsIncompleteOperationalBackupTarget()
    {
        var manifest = CreateManifest();
        manifest.BackupTarget = new PowerForgeServerBackupTarget
        {
            Type = "filesystem",
            Repository = "invalid",
            Branch = "../main",
            Path = "/absolute",
            Encryption = "none",
            RecipientEnv = "1INVALID",
            Retention = new PowerForgeServerBackupRetention { KeepLatestInTree = 0 }
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("type must be github", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("owner/repository", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("branch contains unsupported", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("safe repository-relative", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("encryption must be age", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("recipientEnv", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("from 1 through 365", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RequiresCompleteStrictSshRepositoryPrerequisites()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository
            {
                Role = "private-application",
                Url = "git@github.com:ExampleOrg/ExampleSite.git",
                Path = "/srv/example",
                BootstrapRequiredFiles = ["/etc/example/id_ed25519"],
                SshIdentityFile = "/etc/example/id_ed25519"
            }
        ];

        var incompletePair = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        Assert.Contains(incompletePair, error => error.Contains("both sshIdentityFile and sshKnownHostsFile", StringComparison.Ordinal));

        manifest.Repositories[0].SshKnownHostsFile = "/etc/example/known_hosts";
        var missingPrerequisite = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        Assert.Contains(missingPrerequisite, error => error.Contains("must include sshKnownHostsFile", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RequiresExactRequiredRevisionCaptureCommand()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository
            {
                Role = "application",
                Url = "https://github.com/ExampleOrg/ExampleSite.git",
                Path = "/srv/example",
                RefCaptureCommandId = "static-source-ref"
            }
        ];
        manifest.Capture!.Commands =
        [
            new PowerForgeServerNamedCommand { Id = "static-source-ref", Command = "printf invalid", Required = false }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("required, non-sensitive capture command", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoreScript_UsesAllowlistAndDeclaredMetadataWithoutHeuristics()
    {
        var script = WebCliCommandHandlers.BuildRestoreSecretsScript(
            "archive$(touch /tmp/pwned).age",
            ["/etc/example/secret.env"],
            [
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "example-secret",
                    Path = "/etc/example/secret.env",
                    Owner = "example-service",
                    Group = "example-service",
                    Mode = "600",
                    RestoreMode = "file"
                },
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "excluded-secret",
                    Path = "/etc/example/excluded.env",
                    Owner = "root",
                    Group = "root",
                    Mode = "600",
                    RestoreMode = "file"
                }
            ]);

        Assert.Contains("default_archive='archive$(touch /tmp/pwned).age'", script, StringComparison.Ordinal);
        Assert.Contains("archive=\"${1:-$default_archive}\"", script, StringComparison.Ordinal);
        Assert.Contains("etc/example/secret.env", script, StringComparison.Ordinal);
        Assert.Contains("Archive path is outside the manifest allowlist", script, StringComparison.Ordinal);
        Assert.Contains("Hard links are not allowed", script, StringComparison.Ordinal);
        Assert.Contains("Symlink target is outside the manifest allowlist", script, StringComparison.Ordinal);
        Assert.Contains("Existing symlink blocks safe restore", script, StringComparison.Ordinal);
        Assert.Contains("Archive member traverses an archive symlink", script, StringComparison.Ordinal);
        Assert.Contains("Archive symlink chains are not allowed", script, StringComparison.Ordinal);
        Assert.Contains("Archive member has unsafe special permission bits", script, StringComparison.Ordinal);
        Assert.Contains("normalized = posixpath.normpath(original)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("lstrip", script, StringComparison.Ordinal);
        Assert.Contains("tar --no-same-owner --no-same-permissions --no-overwrite-dir --no-acls --no-selinux --no-xattrs", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tar --same-owner", script, StringComparison.Ordinal);
        Assert.Contains("chown -h 'example-service:example-service' '/etc/example/secret.env'", script, StringComparison.Ordinal);
        Assert.Contains("chmod 600 '/etc/example/secret.env'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("chmod 640", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/etc/example/excluded.env", script, StringComparison.Ordinal);
        Assert.Contains("Secret restore must run as root", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreScript_PreservesOwnershipComponentsThatAreNotDeclared()
    {
        var script = WebCliCommandHandlers.BuildRestoreSecretsScript(
            "archive.age",
            ["/etc/example/owner.env", "/etc/example/group.env", "/etc/example/both.env"],
            [
                new PowerForgeServerRestoreSecretEntry { Path = "/etc/example/owner.env", Owner = "owner-only" },
                new PowerForgeServerRestoreSecretEntry { Path = "/etc/example/group.env", Group = "group-only" },
                new PowerForgeServerRestoreSecretEntry { Path = "/etc/example/both.env", Owner = "owner", Group = "group" }
            ]);

        Assert.Contains("chown -h 'owner-only' '/etc/example/owner.env'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("chown -h 'owner-only:'", script, StringComparison.Ordinal);
        Assert.Contains("chown -h ':group-only' '/etc/example/group.env'", script, StringComparison.Ordinal);
        Assert.Contains("chown -h 'owner:group' '/etc/example/both.env'", script, StringComparison.Ordinal);
    }

    private static PowerForgeServerRecoveryManifest CreateManifest()
        => new()
        {
            SchemaVersion = 1,
            Name = "example",
            Target = new PowerForgeServerTarget { Host = "example.test" },
            Secrets =
            [
                new PowerForgeServerSecret
                {
                    Id = "example-secret",
                    Path = "/etc/example/secret.env",
                    Capture = "encrypted",
                    RestoreMode = "file",
                    Owner = "example-service",
                    Group = "example-service",
                    Mode = "600"
                }
            ],
            Capture = new PowerForgeServerCapture
            {
                PlainFiles =
                [
                    new PowerForgeServerManagedFile { Target = "/etc/example/public.conf", Required = true, Sensitive = false }
                ],
                EncryptedFiles =
                [
                    new PowerForgeServerManagedFile { Target = "/etc/example/secret.env", Required = true, Sensitive = true }
                ]
            }
        };
}
