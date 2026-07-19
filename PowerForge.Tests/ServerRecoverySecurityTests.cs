using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class ServerRecoverySecurityTests
{
    [Fact]
    public void ManifestValidation_RequiresCurrentRecoverySchemaVersion()
    {
        var manifest = CreateManifest();
        manifest.SchemaVersion = 1;

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("schemaVersion must be 2", StringComparison.Ordinal));
    }

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
    public void ManifestValidation_AcceptsCanonicalNumericIdentitiesAndRejectsAmbiguousIds()
    {
        var manifest = CreateManifest();
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "numeric-state",
                Path = "/var/lib/example-numeric",
                Kind = "directory",
                Owner = "0",
                Group = "65534",
                Mode = "0750"
            }
        ];

        Assert.Empty(WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest));

        manifest.Paths[0].Owner = "00";
        manifest.Paths[0].Group = uint.MaxValue.ToString();
        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("invalid owner", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid group", StringComparison.Ordinal));
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
    public void ManifestValidation_RejectsTargetsNestedBelowFilesButAllowsDirectoryNesting()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "config-file",
                Path = "/opt/powerforge/config",
                Source = "/srv/example/deploy/config",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0644"
            },
            new PowerForgeServerPath
            {
                Id = "config-child",
                Path = "/opt/powerforge/config/child",
                Kind = "directory",
                Owner = "root",
                Group = "root",
                Mode = "0755"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("Managed file target '/opt/powerforge/config' must not contain managed target '/opt/powerforge/config/child'", StringComparison.Ordinal));

        manifest.Paths[0].Source = null;
        manifest.Paths[0].Kind = "directory";
        Assert.Empty(WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest));
    }

    [Fact]
    public void ManifestValidation_AcceptsRepositoryOwnedManagedFileSource()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "public-config",
                Path = "/etc/example/public.conf",
                Source = "/srv/example/deploy/public.conf",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0644"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Empty(errors);
    }

    [Fact]
    public void ManifestValidation_RejectsUnsafeOrSecretManagedFileSources()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "secret-config",
                Path = "/etc/example/secret.env",
                Source = "/tmp/untracked-secret.env",
                Kind = "directory",
                Validation = "arbitrary-command"
            },
            new PowerForgeServerPath
            {
                Id = "invalid-sudoers",
                Path = "/tmp/powerforge-example",
                Source = "/srv/example/deploy/powerforge-example.sudoers",
                Kind = "file",
                Owner = "example",
                Group = "example",
                Mode = "0644",
                Validation = "sudoers"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("source must be inside a declared repository", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must use kind 'file'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must declare owner, group, and mode", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unsupported validation", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("overlaps encrypted capture path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("overlaps secret 'example-secret'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("root-owned 0440 file with a dot-free name directly below /etc/sudoers.d", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsUntaggedSudoersTargets()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "untagged-sudoers",
                Path = "/etc/sudoers.d/powerforge-example",
                Source = "/srv/example/deploy/powerforge-example.sudoers",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0440"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("must declare validation 'sudoers'", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsSudoersTargetsOutsideManagedPaths()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Apache = new PowerForgeServerApache
        {
            Sites =
            [
                new PowerForgeServerApacheFile
                {
                    Source = "/srv/example/deploy/apache.sudoers",
                    Target = "/etc/sudoers.d/powerforge-apache"
                }
            ]
        };
        manifest.Systemd = new PowerForgeServerSystemd
        {
            Services =
            [
                new PowerForgeServerSystemdUnit
                {
                    Name = "powerforge-example.service",
                    Source = "/srv/example/deploy/example.service",
                    Target = "/etc/sudoers"
                }
            ]
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("apache.sites[0].target must not manage", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("systemd.units[0].target must not manage", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsManagedSourcesThatReadSecretsOrWriteIntoRepositories()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" },
            new PowerForgeServerRepository { Role = "secret-source", Path = "/etc/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "repository-target",
                Path = "/srv/example/generated/public.conf",
                Source = "/srv/example/deploy/public.conf",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0644"
            },
            new PowerForgeServerPath
            {
                Id = "secret-source",
                Path = "/usr/local/share/example/secret.env",
                Source = "/etc/example/secret.env",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0600"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("target must not overlap declared repository paths", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("source '/etc/example/secret.env' overlaps encrypted capture path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("source '/etc/example/secret.env' overlaps secret 'example-secret'", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsManagedTargetsThatContainRepositoryRoots()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "repository-parent-target",
                Path = "/srv",
                Source = "/srv/example/deploy/public.conf",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0644"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("target must not overlap declared repository paths", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_AppliesRepositorySafetyToApacheAndSystemdFiles()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Apache = new PowerForgeServerApache
        {
            Sites =
            [
                new PowerForgeServerApacheFile
                {
                    Source = "/etc/example/secret.env",
                    Target = "/srv"
                }
            ]
        };
        manifest.Systemd = new PowerForgeServerSystemd
        {
            Services =
            [
                new PowerForgeServerSystemdUnit
                {
                    Name = "example.service",
                    Source = "/srv/example/deploy/example.service",
                    Target = "/srv/example/generated/example.service"
                }
            ]
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("apache.sites[0].source must be inside a declared repository path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apache.sites[0].target must not overlap declared repository paths", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("systemd.units[0].target must not overlap declared repository paths", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apache.sites[0]", StringComparison.Ordinal) && error.Contains("overlaps secret", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_AllowsTargetOnlyApacheObservationEntries()
    {
        var manifest = CreateManifest();
        manifest.Apache = new PowerForgeServerApache
        {
            Conf =
            [
                new PowerForgeServerApacheFile
                {
                    Target = "/etc/apache2/conf-available/platform-managed.conf",
                    Required = true
                }
            ]
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.DoesNotContain(errors, error => error.Contains("apache.files", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RequiresManagedFileSourcesBelowRepositoryRoots()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "repository-root-source",
                Path = "/etc/example/public.conf",
                Source = "/srv/example",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0644"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("source must be inside a declared repository path", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsTrailingSeparatorsOnManagedFilePaths()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" },
            new PowerForgeServerRepository { Role = "trailing", Path = "/srv/trailing/" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "managed-directory",
                Path = "/var/lib/example/",
                Kind = "directory",
                Owner = "root",
                Group = "root",
                Mode = "0755"
            },
            new PowerForgeServerPath
            {
                Id = "managed-file",
                Path = "/etc/example/public.conf/",
                Source = "/srv/example/deploy/public.conf/",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0644"
            }
        ];
        manifest.Apache = new PowerForgeServerApache
        {
            Sites =
            [
                new PowerForgeServerApacheFile
                {
                    Source = "/srv/example/deploy/apache.conf/",
                    Target = "/etc/apache2/sites-available/example.conf/"
                }
            ]
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("repositories[1].path must not end with '/'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Managed path 'managed-directory' target must not end with '/'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Managed path 'managed-file' source must not end with '/'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Managed path 'managed-file' target must not end with '/'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apache.sites[0].source must not end with '/'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apache.sites[0].target must not end with '/'", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsSudoersNamesIgnoredByIncludedir()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Paths =
        [
            new PowerForgeServerPath
            {
                Id = "ignored-sudoers-name",
                Path = "/etc/sudoers.d/powerforge.example",
                Source = "/srv/example/deploy/powerforge-example.sudoers",
                Kind = "file",
                Owner = "root",
                Group = "root",
                Mode = "0440",
                Validation = "sudoers"
            }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("dot-free name", StringComparison.Ordinal));
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
    public void ManifestValidation_RejectsDuplicateOrNestedRepositoryRoots()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "outer", Path = "/srv/example" },
            new PowerForgeServerRepository { Role = "nested", Path = "/srv/example/source" },
            new PowerForgeServerRepository { Role = "duplicate", Path = "/srv/example" }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("repositories[0].path and repositories[1].path must not be duplicate or nested", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("repositories[0].path and repositories[2].path must not be duplicate or nested", StringComparison.Ordinal));
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

    private static PowerForgeServerRecoveryManifest CreateManifest()
        => new()
        {
            SchemaVersion = 2,
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
