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
                new PowerForgeServerManagedFile
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

        Assert.Contains(errors, error => error.Contains("apache.files[0].source must be inside a declared repository path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apache.files[0].target must not overlap declared repository paths", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("systemd.units[0].target must not overlap declared repository paths", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apache.files[0]", StringComparison.Ordinal) && error.Contains("overlaps secret", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_AllowsTargetOnlyApacheObservationEntries()
    {
        var manifest = CreateManifest();
        manifest.Apache = new PowerForgeServerApache
        {
            Conf =
            [
                new PowerForgeServerManagedFile
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
    public void ManifestValidation_RejectsUnsafePackagesAndUnsupportedRuntimeTargets()
    {
        var manifest = CreateManifest();
        manifest.Target!.Os = "debian-13";
        manifest.Target.Architecture = "arm64";
        manifest.Packages = new PowerForgeServerPackages
        {
            Apt = ["curl;touch-pwned"],
            ApacheModules = ["rewrite;touch-pwned"],
            DotnetSdks = ["10.0;touch-pwned"],
            Powershell = true
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("target.os", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("safe Debian package name", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Apache module", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("packages.dotnetSdks", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("target.os ubuntu-24.04", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("target.architecture x64", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_AcceptsSupportedUbuntuRuntimePackages()
    {
        var manifest = CreateManifest();
        manifest.Target!.Os = "ubuntu-24.04";
        manifest.Target.Architecture = "x64";
        manifest.Packages = new PowerForgeServerPackages
        {
            Apt = ["ca-certificates", "libssl3t64:amd64"],
            ApacheModules = ["proxy_http"],
            DotnetSdks = ["8", "10.0"],
            Powershell = true
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Empty(errors);
    }

    [Fact]
    public void ManifestValidation_RejectsDotnetOutsideTheProvenUbuntuLtsLane()
    {
        var manifest = CreateManifest();
        manifest.Target!.Os = "ubuntu-22.04";
        manifest.Target.Architecture = "x64";
        manifest.Packages = new PowerForgeServerPackages { DotnetSdks = ["10.0"] };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("packages.dotnetSdks currently requires target.os ubuntu-24.04", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("8.1")]
    [InlineData("10.99")]
    public void ManifestValidation_RejectsImpossibleDotnetSdkPackageVersions(string version)
    {
        var manifest = CreateManifest();
        manifest.Packages = new PowerForgeServerPackages { DotnetSdks = [version] };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("supported Ubuntu 24.04 LTS SDK band", StringComparison.Ordinal));
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
        Assert.Contains("chown -h 'example-service:example-service' -- '/etc/example/secret.env'", script, StringComparison.Ordinal);
        Assert.Contains("chmod 600 -- '/etc/example/secret.env'", script, StringComparison.Ordinal);
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

        Assert.Contains("chown -h 'owner-only' -- '/etc/example/owner.env'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("chown -h 'owner-only:'", script, StringComparison.Ordinal);
        Assert.Contains("chown -h ':group-only' -- '/etc/example/group.env'", script, StringComparison.Ordinal);
        Assert.Contains("chown -h 'owner:group' -- '/etc/example/both.env'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreScript_AppliesDirectorySecretMetadataRecursivelyWithoutFollowingLinks()
    {
        var script = WebCliCommandHandlers.BuildRestoreSecretsScript(
            "archive.age",
            ["/etc/example/private"],
            [
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "service-configuration",
                    Path = "/etc/example/private",
                    Owner = "root",
                    Group = "example-service",
                    Mode = "750",
                    RestoreMode = "directory"
                }
            ]);

        Assert.Contains("os.O_NOFOLLOW", script, StringComparison.Ordinal);
        Assert.Contains("os.open(component, flags, dir_fd=current_fd)", script, StringComparison.Ordinal);
        Assert.Contains("os.fchown(member_fd, uid, gid)", script, StringComparison.Ordinal);
        Assert.Contains("os.fchmod(member_fd, int(directory_mode, 8))", script, StringComparison.Ordinal);
        Assert.Contains("normalized != root_normalized and not normalized.startswith(root_normalized + '/')", script, StringComparison.Ordinal);
        Assert.Contains("apply_directory_metadata \"$tmp_dir/secrets.tar.gz\" '/etc/example/private' 'root' 'example-service' '750' '640'", script, StringComparison.Ordinal);
        Assert.Contains("int(value, 10) if value.isdecimal()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("follow_symlinks=False", script, StringComparison.Ordinal);
        Assert.DoesNotContain("find -P", script, StringComparison.Ordinal);
        Assert.DoesNotContain("chown -hR", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreScript_AppliesOverlappingSecretMetadataFromParentToChild()
    {
        var script = WebCliCommandHandlers.BuildRestoreSecretsScript(
            "archive.age",
            ["/etc/example", "/etc/example/private", "/etc/example/private/key"],
            [
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "private-directory",
                    Path = "/etc/example/private",
                    Owner = "root",
                    Group = "private-service",
                    Mode = "700",
                    RestoreMode = "directory"
                },
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "private-key",
                    Path = "/etc/example/private/key",
                    Owner = "root",
                    Group = "root",
                    Mode = "600",
                    RestoreMode = "file"
                },
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "service-directory",
                    Path = "/etc/example///",
                    Owner = "root",
                    Group = "example-service",
                    Mode = "750",
                    RestoreMode = "directory"
                }
            ]);

        var parentIndex = script.IndexOf("apply_directory_metadata \"$tmp_dir/secrets.tar.gz\" '/etc/example' ", StringComparison.Ordinal);
        var childIndex = script.IndexOf("apply_directory_metadata \"$tmp_dir/secrets.tar.gz\" '/etc/example/private' ", StringComparison.Ordinal);
        var fileIndex = script.IndexOf("chown -h 'root:root' -- '/etc/example/private/key'", StringComparison.Ordinal);

        Assert.True(parentIndex >= 0 && parentIndex < childIndex, "Parent directory metadata must be applied before child metadata.");
        Assert.True(childIndex < fileIndex, "Exact child file metadata must be applied after enclosing directory metadata.");
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
