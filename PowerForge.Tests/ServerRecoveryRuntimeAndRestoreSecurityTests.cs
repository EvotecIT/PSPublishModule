using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class ServerRecoverySecurityTests
{
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
        Assert.Contains("if original != normalized:", script, StringComparison.Ordinal);
        Assert.Contains("Non-canonical archive path", script, StringComparison.Ordinal);
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

    [Fact]
    public void RestoreScript_StagesRepositorySecretsInsteadOfPrecreatingCloneTarget()
    {
        const string stagingRoot = "/var/lib/powerforge/restore-secrets/fixture";
        var script = WebCliCommandHandlers.BuildRestoreSecretsScript(
            "archive.age",
            ["/srv/example/.deploy-key"],
            [
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "repository-key",
                    Path = "/srv/example/.deploy-key",
                    Owner = "root",
                    Group = "root",
                    Mode = "600",
                    RestoreMode = "file",
                    RestoreAfterRepositories = true,
                    StagedPath = stagingRoot + "/srv/example/.deploy-key"
                }
            ],
            stagingRoot);

        Assert.Contains("staging_root='/var/lib/powerforge/restore-secrets/fixture'", script, StringComparison.Ordinal);
        Assert.Contains("if [ ! -e /var/lib/powerforge ]", script, StringComparison.Ordinal);
        Assert.Contains("Secret staging base is not a root-controlled directory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("0700 /var/lib/powerforge /var/lib/powerforge/restore-secrets", script, StringComparison.Ordinal);
        Assert.Contains("--exclude='srv/example/.deploy-key'", script, StringComparison.Ordinal);
        Assert.Contains("-C \"$staging_root\" -- 'srv/example/.deploy-key'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("chown -h 'root:root' -- '/srv/example/.deploy-key'", script, StringComparison.Ordinal);
        Assert.Contains("run the generated bootstrap plan to install them after clone", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreScript_ExtractsOnlyOptionalDeferredSecretsPresentInTheValidatedArchive()
    {
        const string stagingRoot = "/var/lib/powerforge/restore-secrets/fixture";
        var script = WebCliCommandHandlers.BuildRestoreSecretsScript(
            "archive.age",
            ["/srv/example/runtime/optional.env"],
            [
                new PowerForgeServerRestoreSecretEntry
                {
                    Id = "optional-runtime-secret",
                    Path = "/srv/example/runtime/optional.env",
                    RequiredDuringBootstrap = false,
                    RestoreMode = "file",
                    RestoreAfterRepositories = true,
                    StagedPath = stagingRoot + "/srv/example/runtime/optional.env"
                }
            ],
            stagingRoot);

        Assert.Contains("optional-deferred-paths", script, StringComparison.Ordinal);
        Assert.Contains("present-optional-deferred-paths", script, StringComparison.Ordinal);
        Assert.Contains("if normalized in optional_deferred:", script, StringComparison.Ordinal);
        Assert.Contains("stream.write(path.encode('utf-8') + b'\\0')", script, StringComparison.Ordinal);
        Assert.Contains("if [ -s \"$tmp_dir/present-optional-deferred-paths\" ]", script, StringComparison.Ordinal);
        Assert.Contains("--null --verbatim-files-from", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-C \"$staging_root\" -- 'srv/example/runtime/optional.env'", script, StringComparison.Ordinal);
    }
}
