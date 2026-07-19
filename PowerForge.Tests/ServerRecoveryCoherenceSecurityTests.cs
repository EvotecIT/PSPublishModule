using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class ServerRecoverySecurityTests
{
    [Fact]
    public void ManifestValidation_RequiresSharedOperationLockForCaptureAndDeploy()
    {
        var manifest = CreateManifest();
        manifest.Deploy = new PowerForgeServerDeploy
        {
            Commands = [new PowerForgeServerNamedCommand { Id = "deploy", Command = "/usr/local/sbin/deploy-site" }]
        };

        var missingLockErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(missingLockErrors, error => error.Contains("operationLocks is required", StringComparison.Ordinal));

        manifest.OperationLocks = ["/var/lock/powerforge-site-example.lock"];

        Assert.Empty(WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest));

        manifest.OperationLocks = ["/var/lock/powerforge-site-example.lock", "/tmp/example.lock"];
        var invalidLockErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(invalidLockErrors, error => error.Contains("directly below /var/lock", StringComparison.Ordinal));

        manifest.OperationLocks = [$"/var/lock/{new string('a', 126)}.lock"];
        Assert.Empty(WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest));

        foreach (var invalidName in new[] { ".lock", "_site.lock", "-site.lock" })
        {
            manifest.OperationLocks = [$"/var/lock/{invalidName}"];
            Assert.Contains(
                WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest),
                error => error.Contains("directly below /var/lock", StringComparison.Ordinal));
        }

        manifest.OperationLocks = [$"/var/lock/{new string('a', 127)}.lock"];
        var oversizedLockErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(oversizedLockErrors, error => error.Contains("directly below /var/lock", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RequiresExplicitCommandOwnedLockingForNestedSiteDeploy()
    {
        var manifest = CreateManifest();
        manifest.OperationLocks = ["/var/lock/powerforge-site-example.lock"];
        manifest.Deploy = new PowerForgeServerDeploy
        {
            Commands =
            [
                new PowerForgeServerNamedCommand
                {
                    Id = "deploy",
                    Command = "sudo -n /usr/local/sbin/powerforge-site-deploy --site example"
                }
            ]
        };

        var engineOwnedErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.Deploy.OperationLockOwner = "command";
        var commandOwnedErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.Deploy.Commands = [.. manifest.Deploy.Commands, new PowerForgeServerNamedCommand { Id = "extra", Command = "true" }];
        var multipleCommandErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(engineOwnedErrors, error => error.Contains("avoid nested lock acquisition", StringComparison.Ordinal));
        Assert.Empty(commandOwnedErrors);
        Assert.Contains(multipleCommandErrors, error => error.Contains("exactly one non-sensitive deploy command", StringComparison.Ordinal));

        manifest.Deploy.Commands =
        [
            new PowerForgeServerNamedCommand
            {
                Id = "deploy",
                Command = "sudo -n /usr/local/sbin/powerforge-site-deploy --site other"
            }
        ];
        var mismatchedLockErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.OperationLocks =
        [
            "/var/lock/powerforge-site-other.lock",
            "/var/lock/powerforge-contact-other.lock"
        ];
        var extraLockErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.OperationLocks = ["/var/lock/powerforge-site-other.lock"];
        manifest.Deploy.Commands[0].Command = "sudo -n /usr/local/sbin/powerforge-site-deploy --site-id other";
        var nonCanonicalErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.Deploy.Commands[0].Command = "sudo -n /usr/local/sbin/powerforge-site-deploy --site other --site example";
        var duplicateSiteErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.Deploy.Commands[0].Command = "sudo -n /usr/local/sbin/powerforge-site-deploy --site other; true";
        var shellSyntaxErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        manifest.Deploy.Commands[0].Command = "sudo -n /usr/local/sbin/./powerforge-site-deploy --site other";
        var equivalentPathErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(mismatchedLockErrors, error => error.Contains("requires exactly operationLocks", StringComparison.Ordinal));
        Assert.Contains(extraLockErrors, error => error.Contains("requires exactly operationLocks", StringComparison.Ordinal));
        Assert.Contains(nonCanonicalErrors, error => error.Contains("canonical", StringComparison.Ordinal));
        Assert.Contains(duplicateSiteErrors, error => error.Contains("canonical", StringComparison.Ordinal));
        Assert.Contains(shellSyntaxErrors, error => error.Contains("canonical", StringComparison.Ordinal));
        Assert.Contains(equivalentPathErrors, error => error.Contains("canonical", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RequiresRepositorySecretsToRestoreAfterClone()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" }
        ];
        manifest.Secrets =
        [
            new PowerForgeServerSecret
            {
                Id = "repository-key",
                Path = "/srv/example/.deploy-key",
                Capture = "encrypted",
                RestoreMode = "file",
                Owner = "root",
                Group = "root",
                Mode = "600"
            }
        ];
        manifest.Capture!.EncryptedFiles =
        [
            new PowerForgeServerManagedFile { Target = "/srv/example/.deploy-key", Required = true, Sensitive = true }
        ];

        var unsafeErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(unsafeErrors, error => error.Contains("must set restoreAfterRepositories=true", StringComparison.Ordinal));

        manifest.Secrets[0].RestoreAfterRepositories = true;

        Assert.Empty(WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest));

        manifest.Capture.EncryptedFiles[0].Target = "/srv/example";
        var ancestorCaptureErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(ancestorCaptureErrors, error => error.Contains("must have one exact capture.encryptedFiles entry", StringComparison.Ordinal));
        Assert.Contains(ancestorCaptureErrors, error => error.Contains("without an exact deferred secret contract", StringComparison.Ordinal));

        manifest.Capture.EncryptedFiles[0].Target = "/srv/example/.deploy-key";
        manifest.Secrets[0].Capture = "exclude";
        var excludedDeferredErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(excludedDeferredErrors, error => error.Contains("must use capture 'encrypted'", StringComparison.Ordinal));

        manifest.Secrets[0].Capture = "encrypted";
        manifest.Secrets[0].RestoreMode = "directory";
        var directoryErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(directoryErrors, error => error.Contains("must use restoreMode 'file'", StringComparison.Ordinal));

        manifest.Secrets[0].Path = "/srv/example";
        manifest.Secrets[0].RestoreMode = "file";
        manifest.Capture.EncryptedFiles[0].Target = "/srv/example";
        var repositoryRootErrors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(repositoryRootErrors, error => error.Contains("must not replace declared repository root", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_ReportsNestedRepositoriesWithoutThrowingForDeferredSecrets()
    {
        var manifest = CreateManifest();
        manifest.Repositories =
        [
            new PowerForgeServerRepository { Role = "application", Path = "/srv/example" },
            new PowerForgeServerRepository { Role = "nested", Path = "/srv/example/nested" }
        ];
        manifest.Secrets =
        [
            new PowerForgeServerSecret
            {
                Id = "nested-secret",
                Path = "/srv/example/nested/.secret",
                Capture = "encrypted",
                RestoreAfterRepositories = true,
                RestoreMode = "file",
                Owner = "root",
                Group = "root",
                Mode = "600"
            }
        ];
        manifest.Capture!.EncryptedFiles =
        [
            new PowerForgeServerManagedFile { Target = "/srv/example/nested/.secret", Required = true, Sensitive = true }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("must not be duplicate or nested", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/etc/letsencrypt")]
    [InlineData("/etc/letsencrypt/accounts")]
    [InlineData("/etc/letsencrypt/accounts/acme-v02.api.letsencrypt.org/directory")]
    [InlineData("/etc/letsencrypt/accounts/acme-v02.api.letsencrypt.org/directory/account/private_key.json")]
    [InlineData("/etc/letsencrypt/archive")]
    [InlineData("/etc/letsencrypt/archive/example.com/privkey1.pem")]
    [InlineData("/etc/letsencrypt/live")]
    [InlineData("/etc/letsencrypt/live/example.com/privkey.pem")]
    public void ManifestValidation_RejectsOverbroadLetsEncryptCapturePaths(string path)
    {
        var manifest = CreateManifest();
        manifest.Secrets =
        [
            new PowerForgeServerSecret
            {
                Id = "acme-state",
                Path = path,
                Capture = "encrypted",
                RestoreMode = "directory",
                Owner = "root",
                Group = "root",
                Mode = "700"
            }
        ];
        manifest.Capture!.EncryptedFiles =
        [
            new PowerForgeServerManagedFile { Target = path, Required = true, Sensitive = true }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("too broad", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/etc/letsencrypt")]
    [InlineData("/etc/letsencrypt/accounts/example/account")]
    [InlineData("/etc/letsencrypt/archive/example.com")]
    [InlineData("/etc/letsencrypt/live/example.com")]
    public void ManifestValidation_RejectsLetsEncryptPrivateStateInPlainCapture(string path)
    {
        var manifest = CreateManifest();
        manifest.Capture!.PlainFiles =
        [
            new PowerForgeServerManagedFile { Target = path, Required = true, Sensitive = false }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("must use encrypted capture", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsWildcardPlainCaptureBeforeShellExpansion()
    {
        var manifest = CreateManifest();
        manifest.Capture!.PlainFiles =
        [
            new PowerForgeServerManagedFile { Target = "/etc/letsencrypt/*", Required = true, Sensitive = false }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("must use an exact path", StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestValidation_RejectsApacheFilenameBeyondFilesystemLimit()
    {
        var manifest = CreateManifest();
        manifest.Apache = new PowerForgeServerApache
        {
            Sites =
            [
                new PowerForgeServerApacheFile
                {
                    Target = "/etc/apache2/sites-available/" + new string('a', 251) + ".conf"
                }
            ]
        };

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("at most 255 bytes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/etc/letsencrypt/accounts/acme-v02.api.letsencrypt.org/directory/account")]
    [InlineData("/etc/letsencrypt/archive/example.com")]
    [InlineData("/etc/letsencrypt/live/example.com")]
    public void ManifestValidation_AllowsOneExactLetsEncryptScope(string path)
    {
        var manifest = CreateManifest();
        manifest.Secrets =
        [
            new PowerForgeServerSecret
            {
                Id = "acme-state",
                Path = path,
                Capture = "encrypted",
                RestoreMode = "directory",
                Owner = "root",
                Group = "root",
                Mode = "700"
            }
        ];
        manifest.Capture!.EncryptedFiles =
        [
            new PowerForgeServerManagedFile { Target = path, Required = true, Sensitive = true }
        ];

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.DoesNotContain(errors, error => error.Contains("too broad", StringComparison.Ordinal));
    }
}
