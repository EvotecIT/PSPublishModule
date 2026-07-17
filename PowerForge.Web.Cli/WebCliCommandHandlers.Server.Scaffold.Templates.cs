using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static PowerForgeServerRecoveryManifest BuildServerScaffoldManifest(PowerForgeServerScaffoldOptions options)
    {
        var deploymentUser = $"powerforge-{options.SiteId}";
        var backupUser = $"{deploymentUser}-backup";
        var repositoryPath = $"/srv/powerforge/sources/{options.SiteId}";
        var enginePath = "/srv/powerforge/engine";
        var siteRoot = $"/var/www/{options.SiteId}/site";
        var domainFile = options.Domain.Replace('.', '-');
        var siteEnvironment = $"/etc/powerforge/sites/{options.Domain}.env";
        var repositoryKey = $"/etc/powerforge/repository-ssh/{options.SiteId}_ed25519";
        var repositoryUrl = options.PrivateRepository
            ? $"git@github.com:{options.Repository}.git"
            : $"https://github.com/{options.Repository}.git";

        var paths = new List<PowerForgeServerPath>
        {
            new() { Id = "static-releases", Path = $"{siteRoot}/releases", Kind = "directory", Owner = "root", Group = "root", Mode = "755" },
            new() { Id = "static-current", Path = $"{siteRoot}/current", Kind = "symlink", Owner = "root", Group = "root" },
            new() { Id = "static-pending-state", Path = "/var/lib/powerforge/site-pending", Kind = "directory", Owner = "root", Group = "root", Mode = "700" },
            new() { Id = "powerforge-sites-config", Path = "/etc/powerforge/sites", Kind = "directory", Owner = "root", Group = "root", Mode = "750" },
            new() { Id = "deployment-user-home", Path = $"/home/{deploymentUser}", Kind = "directory", Owner = deploymentUser, Group = deploymentUser, Mode = "750" },
            new() { Id = "deployment-user-ssh", Path = $"/home/{deploymentUser}/.ssh", Kind = "directory", Owner = deploymentUser, Group = deploymentUser, Mode = "700" },
            new() { Id = "deployment-user-authorized-keys", Path = $"/home/{deploymentUser}/.ssh/authorized_keys", Source = $"{repositoryPath}/deploy/linux/powerforge-{options.SiteId}-authorized_keys", Kind = "file", Owner = deploymentUser, Group = deploymentUser, Mode = "600" },
            new() { Id = "backup-user-home", Path = $"/var/lib/{backupUser}", Kind = "directory", Owner = backupUser, Group = backupUser, Mode = "700" },
            new() { Id = "backup-user-ssh", Path = $"/var/lib/{backupUser}/.ssh", Kind = "directory", Owner = backupUser, Group = backupUser, Mode = "700" },
            new() { Id = "backup-user-authorized-keys", Path = $"/var/lib/{backupUser}/.ssh/authorized_keys", Source = $"{repositoryPath}/deploy/linux/powerforge-{options.SiteId}-backup-authorized_keys", Kind = "file", Owner = backupUser, Group = backupUser, Mode = "600" },
            new() { Id = "static-deploy-runtime", Path = "/usr/local/sbin/powerforge-site-deploy", Source = $"{enginePath}/Deployment/Linux/powerforge-site-deploy.sh", Kind = "file", Owner = "root", Group = "root", Mode = "755" },
            new() { Id = "static-reconcile-runtime", Path = "/usr/local/sbin/powerforge-site-reconcile", Source = $"{enginePath}/Deployment/Linux/powerforge-site-reconcile.sh", Kind = "file", Owner = "root", Group = "root", Mode = "755" },
            new() { Id = "encrypted-capture-runtime", Path = "/usr/local/sbin/powerforge-server-encrypted-capture", Source = $"{enginePath}/Deployment/Linux/powerforge-server-encrypted-capture.sh", Kind = "file", Owner = "root", Group = "root", Mode = "755" },
            new() { Id = "apache-site-enable-runtime", Path = "/usr/local/sbin/powerforge-apache-site-enable", Source = $"{enginePath}/Deployment/Linux/powerforge-apache-site-enable.sh", Kind = "file", Owner = "root", Group = "root", Mode = "755" },
            new() { Id = "static-deploy-config", Path = siteEnvironment, Source = $"{repositoryPath}/deploy/linux/{options.Domain}.env", Kind = "file", Owner = "root", Group = "root", Mode = "640" },
            new() { Id = "deployment-sudoers", Path = $"/etc/sudoers.d/{deploymentUser}", Source = $"{repositoryPath}/deploy/linux/powerforge-{options.SiteId}.sudoers", Kind = "file", Owner = "root", Group = "root", Mode = "440", Validation = "sudoers" },
            new() { Id = "backup-sudoers", Path = $"/etc/sudoers.d/{backupUser}", Source = $"{repositoryPath}/deploy/linux/powerforge-{options.SiteId}-backup.sudoers", Kind = "file", Owner = "root", Group = "root", Mode = "440", Validation = "sudoers" }
        };
        if (options.PrivateRepository)
        {
            paths.AddRange([
                new PowerForgeServerPath { Id = "repository-ssh-directory", Path = "/etc/powerforge/repository-ssh", Kind = "directory", Owner = "root", Group = "root", Mode = "700" },
                new PowerForgeServerPath { Id = "repository-known-hosts", Path = "/etc/powerforge/repository-ssh/github_known_hosts", Source = $"{repositoryPath}/deploy/linux/github_known_hosts", Kind = "file", Owner = "root", Group = "root", Mode = "600" },
                new PowerForgeServerPath { Id = "repository-private-key", Path = repositoryKey, Kind = "file", Owner = "root", Group = "root", Mode = "600" }
            ]);
        }

        var plainFiles = new List<PowerForgeServerManagedFile>
        {
            RequiredFile($"/etc/apache2/sites-available/{domainFile}.conf"),
            RequiredFile($"/etc/apache2/sites-available/{domainFile}-le-ssl.conf"),
            RequiredFile("/etc/systemd/system/powerforge-site-reconcile.service"),
            RequiredFile("/etc/systemd/system/powerforge-site-reconcile.timer"),
            RequiredFile(siteEnvironment),
            RequiredFile($"/etc/sudoers.d/{deploymentUser}"),
            RequiredFile($"/etc/sudoers.d/{backupUser}"),
            RequiredFile($"/etc/letsencrypt/renewal/{options.Domain}.conf"),
            RequiredFile("/etc/letsencrypt/options-ssl-apache.conf")
        };
        if (options.PrivateRepository)
        {
            plainFiles.Add(RequiredFile("/etc/powerforge/repository-ssh/github_known_hosts"));
        }

        var encryptedFiles = new List<PowerForgeServerManagedFile>
        {
            RequiredSecretFile("/etc/letsencrypt/accounts"),
            RequiredSecretFile($"/etc/letsencrypt/archive/{options.Domain}"),
            RequiredSecretFile($"/etc/letsencrypt/live/{options.Domain}")
        };
        var secrets = new List<PowerForgeServerSecret>
        {
            new()
            {
                Id = $"letsencrypt-{options.SiteId}-private-keys",
                Path = $"/etc/letsencrypt/archive/{options.Domain}",
                RequiredFor = ["certificate-continuity"],
                RequiredDuringBootstrap = false,
                Capture = "encrypted",
                RestoreMode = "directory"
            },
            new()
            {
                Id = "letsencrypt-acme-account",
                Path = "/etc/letsencrypt/accounts",
                RequiredFor = ["certificate-renewal"],
                RequiredDuringBootstrap = false,
                Capture = "encrypted",
                RestoreMode = "directory"
            }
        };
        if (options.PrivateRepository)
        {
            encryptedFiles.Add(RequiredSecretFile(repositoryKey));
            secrets.Add(new PowerForgeServerSecret
            {
                Id = $"{options.SiteId}-repository-private-key",
                Path = repositoryKey,
                RequiredFor = ["private-repository-recovery"],
                Capture = "encrypted",
                RestoreMode = "file",
                Owner = "root",
                Group = "root",
                Mode = "600"
            });
        }

        var repositoryPrerequisites = options.PrivateRepository
            ? new[] { "/etc/powerforge/repository-ssh/github_known_hosts", repositoryKey }
            : null;
        var applicationRepository = new PowerForgeServerRepository
        {
            Role = "application-and-website",
            Url = repositoryUrl,
            Path = repositoryPath,
            Branch = options.Branch,
            Ref = options.RepositoryRef,
            RefCaptureCommandId = "static-source-ref",
            Required = true,
            BootstrapRequiredFiles = repositoryPrerequisites,
            SshIdentityFile = options.PrivateRepository ? repositoryKey : null,
            SshKnownHostsFile = options.PrivateRepository ? "/etc/powerforge/repository-ssh/github_known_hosts" : null
        };
        var repositoryVerification = options.PrivateRepository
            ? $"sudo -n env {BuildRepositoryGitPrefix(applicationRepository).TrimEnd()} git ls-remote {ShellQuote(repositoryUrl)} HEAD >/dev/null"
            : $"git ls-remote https://github.com/{options.Repository}.git HEAD >/dev/null";

        return new PowerForgeServerRecoveryManifest
        {
            Schema = $"https://raw.githubusercontent.com/EvotecIT/PSPublishModule/{options.EngineRef}/Schemas/powerforge.web.serverrecovery.schema.json",
            SchemaVersion = 1,
            Name = $"{options.SiteId}-production-linux",
            Description = $"Generated recovery manifest for the {options.Domain} static website.",
            Target = new PowerForgeServerTarget { SshAlias = "powerforge-site-host", Host = options.Host, User = "ubuntu", SshPort = options.SshPort, Os = "ubuntu-24.04", Architecture = "x64" },
            Repositories =
            [
                applicationRepository,
                new PowerForgeServerRepository { Role = "deployment-engine", Url = "https://github.com/EvotecIT/PSPublishModule.git", Path = enginePath, Branch = "main", Ref = options.EngineRef, Required = true }
            ],
            Accounts =
            [
                new PowerForgeServerAccount { Name = deploymentUser, CreateHome = true, Home = $"/home/{deploymentUser}", Shell = "/bin/bash" },
                new PowerForgeServerAccount { Name = backupUser, System = true, CreateHome = true, Home = $"/var/lib/{backupUser}", Shell = "/bin/bash" }
            ],
            Packages = new PowerForgeServerPackages
            {
                Apt = ["age", "apache2", "certbot", "curl", "git", "jq", "python3", "python3-certbot-apache", "rsync", "ufw"],
                ApacheModules = ["headers", "rewrite", "ssl"]
            },
            Paths = paths.ToArray(),
            Apache = new PowerForgeServerApache
            {
                Service = "apache2",
                Modules = ["headers", "rewrite", "ssl"],
                Sites =
                [
                    new PowerForgeServerManagedFile { Source = $"{repositoryPath}/{options.WebsiteRoot}/deploy/apache.conf", Target = $"/etc/apache2/sites-available/{domainFile}.conf", Required = true },
                    new PowerForgeServerManagedFile { Source = $"{repositoryPath}/{options.WebsiteRoot}/deploy/apache-ssl.conf", Target = $"/etc/apache2/sites-available/{domainFile}-le-ssl.conf", Required = true }
                ],
                ValidateCommand = "sudo -n apachectl configtest",
                ReloadCommand = "sudo -n systemctl reload apache2"
            },
            Firewall = new PowerForgeServerFirewall { Provider = "ufw", DefaultIncoming = "deny", DefaultOutgoing = "allow", SshPorts = [options.SshPort], WebOriginPolicy = options.CloudflareEnabled ? "cloudflare-ready" : "operator-managed" },
            Certificates =
            [
                new PowerForgeServerCertificate
                {
                    Name = options.Domain,
                    Domains = options.IncludeWwwAlias ? [options.Domain, $"www.{options.Domain}"] : [options.Domain],
                    Authenticator = "apache",
                    RenewalConfigPath = $"/etc/letsencrypt/renewal/{options.Domain}.conf",
                    DryRunCommand = $"certbot renew --dry-run --cert-name {options.Domain} --non-interactive --agree-tos --no-random-sleep-on-renew",
                    SecretRefs = [$"letsencrypt-{options.SiteId}-private-keys"]
                }
            ],
            Systemd = new PowerForgeServerSystemd
            {
                Services = [new PowerForgeServerSystemdUnit { Name = "powerforge-site-reconcile.service", Source = $"{enginePath}/Deployment/Linux/systemd/powerforge-site-reconcile.service", Target = "/etc/systemd/system/powerforge-site-reconcile.service", Required = true }],
                Timers = [new PowerForgeServerSystemdUnit { Name = "powerforge-site-reconcile.timer", Source = $"{enginePath}/Deployment/Linux/systemd/powerforge-site-reconcile.timer", Target = "/etc/systemd/system/powerforge-site-reconcile.timer", Enabled = true, Required = true }]
            },
            Secrets = secrets.ToArray(),
            Capture = new PowerForgeServerCapture
            {
                PlainFiles = plainFiles.ToArray(),
                EncryptedFiles = encryptedFiles.ToArray(),
                Commands =
                [
                    Command("os-release", "cat /etc/os-release", required: true),
                    Command("packages", "dpkg-query -W -f='${binary:Package}\\t${Version}\\n'", required: true),
                    Command("apache-vhosts", "sudo -n apachectl -S", required: true),
                    Command("release-link", $"readlink -f {siteRoot}/current", required: true),
                    Command("static-source-ref", $"jq -er '.sourceSha | select(type == \"string\" and test(\"^([0-9a-fA-F]{{40}}|[0-9a-fA-F]{{64}})$\"))' {siteRoot}/current/_powerforge/deployment.json", required: true)
                ],
                Exclude = [$"{siteRoot}/releases", $"{repositoryPath}/{options.WebsiteRoot}/_site", $"{repositoryPath}/{options.WebsiteRoot}/_reports", $"{repositoryPath}/{options.WebsiteRoot}/_temp"]
            },
            Deploy = new PowerForgeServerCommandGroup
            {
                Commands =
                [
                    Command("reload-systemd", "sudo -n systemctl daemon-reload", required: true),
                    Command("enable-static-deployment-reconciler", "sudo -n systemctl enable --now powerforge-site-reconcile.timer", required: true),
                    Command("enable-apache-sites", $"sudo -n /usr/local/sbin/powerforge-apache-site-enable --http-site {domainFile}.conf --https-site {domainFile}-le-ssl.conf --certificate-name {options.Domain}", required: true)
                ]
            },
            Verify = new PowerForgeServerVerify
            {
                Commands =
                [
                    Command("apache-config", "sudo -n apachectl configtest", required: true),
                    Command("static-deployment-reconciler", "test -x /usr/local/sbin/powerforge-site-reconcile && systemctl is-enabled --quiet powerforge-site-reconcile.timer && systemctl is-active --quiet powerforge-site-reconcile.timer", required: true),
                    Command("deployment-account", $"id -u {deploymentUser} >/dev/null && sudo -n grep -q '^restrict ' /home/{deploymentUser}/.ssh/authorized_keys && sudo -n visudo -cf /etc/sudoers.d/{deploymentUser}", required: true),
                    Command("backup-account", $"id -u {backupUser} >/dev/null && sudo -n grep -q '^restrict ' /var/lib/{backupUser}/.ssh/authorized_keys && sudo -n visudo -cf /etc/sudoers.d/{backupUser}", required: true),
                    Command("repository-identity", repositoryVerification, required: true),
                    Command("static-provenance", $"test -s {siteRoot}/current/_powerforge/deployment.json", required: true),
                    Command($"certbot-{options.SiteId}-dry-run", $"sudo -n certbot renew --dry-run --cert-name {options.Domain} --non-interactive --agree-tos --no-random-sleep-on-renew", required: true)
                ],
                Urls = [new PowerForgeServerVerifyUrl { Url = $"https://{options.Domain}/", ExpectedStatus = 200, Via = options.CloudflareEnabled ? "cloudflare" : "public" }]
            },
            BackupTarget = new PowerForgeServerBackupTarget
            {
                Type = "github",
                Repository = options.BackupRepository,
                Branch = "main",
                Path = $"linux/{options.SiteId}",
                Encryption = "age",
                Recipient = options.BackupRecipient,
                RecipientEnv = "POWERFORGE_BACKUP_AGE_RECIPIENT",
                Retention = new PowerForgeServerBackupRetention { KeepLatestInTree = 24 }
            },
            Notes =
            [
                "Generated website output and timestamped releases are rebuildable and intentionally excluded from backup state.",
                "Deployment and encrypted recovery capture use separate restricted SSH accounts and protected-environment private keys.",
                options.CloudflareEnabled ? "Cloudflare wiring is declarative; provision a dedicated per-site token outside the repository." : "Cloudflare integration is intentionally disabled until dedicated per-site credentials are provisioned."
            ]
        };
    }

    private static PowerForgeServerManagedFile RequiredFile(string target)
        => new() { Target = target, Required = true, Sensitive = false };

    private static PowerForgeServerManagedFile RequiredSecretFile(string target)
        => new() { Target = target, Required = true, Sensitive = true };

    private static PowerForgeServerNamedCommand Command(string id, string command, bool required = false)
        => new() { Id = id, Command = command, Required = required };

    private static string BuildScaffoldWebsiteWorkflow(PowerForgeServerScaffoldOptions options)
        => ServerScaffoldTemplateStore.Render(
            "website-deploy.yml",
            ("__BRANCH__", options.Branch),
            ("__WEBSITE_ROOT__", options.WebsiteRoot),
            ("__ENGINE_REF__", options.EngineRef),
            ("__DOMAIN__", options.Domain),
            ("__SMOKE_PATHS__", options.SmokePaths),
            ("__CLOUDFLARE_INPUT__", options.CloudflareEnabled ? $"      deployment_cloudflare_zone: {options.Domain}" : string.Empty),
            ("__CLOUDFLARE_SECRET__", options.CloudflareEnabled ? "      deployment_cloudflare_api_token: ${{ secrets.DEPLOYMENT_CLOUDFLARE_API_TOKEN }}" : string.Empty));

    private static string BuildScaffoldBackupWorkflow(PowerForgeServerScaffoldOptions options)
        => ServerScaffoldTemplateStore.Render(
            "server-backup.yml",
            ("__SITE_ID__", options.SiteId),
            ("__ENGINE_REF__", options.EngineRef));

    private static string BuildScaffoldSiteEnvironment(PowerForgeServerScaffoldOptions options)
        => ServerScaffoldTemplateStore.Render(
            "site.env",
            ("__SITE_ID__", options.SiteId),
            ("__DOMAIN__", options.Domain),
            ("__SMOKE_PATHS__", options.SmokePaths),
            ("__CLOUDFLARE_ENABLED__", options.CloudflareEnabled ? "1" : "0"));

    private static string BuildScaffoldDeploymentSudoers(PowerForgeServerScaffoldOptions options)
    {
        var user = $"powerforge-{options.SiteId}";
        var domainPattern = Regex.Escape(options.Domain);
        var tempPattern = $"/tmp/powerforge-[0-9]+-[0-9]+-{domainPattern}";
        return ServerScaffoldTemplateStore.Render(
            "deployment.sudoers",
            ("__DEPLOYMENT_USER__", user),
            ("__DOMAIN_PATTERN__", domainPattern),
            ("__TEMP_PATTERN__", tempPattern));
    }

    private static string BuildScaffoldBackupSudoers(PowerForgeServerScaffoldOptions options, PowerForgeServerRecoveryManifest manifest)
    {
        var alias = "PF_" + Regex.Replace(options.SiteId.ToUpperInvariant(), "[^A-Z0-9]", "_", RegexOptions.CultureInvariant);
        var plain = string.Join(' ', manifest.Capture!.PlainFiles!.Select(static file => file.Target));
        var encryptedCommand = BuildRemoteEncryptedCaptureSudoersCommand(
            manifest.Capture.EncryptedFiles!,
            manifest.BackupTarget!.Recipient!);
        var user = $"powerforge-{options.SiteId}-backup";
        return ServerScaffoldTemplateStore.Render(
            "backup.sudoers",
            ("__ALIAS__", alias),
            ("__PLAIN_PATHS__", plain),
            ("__ENCRYPTED_CAPTURE_COMMAND__", encryptedCommand),
            ("__BACKUP_USER__", user));
    }

    private static string BuildScaffoldApacheHttp(PowerForgeServerScaffoldOptions options)
        => ServerScaffoldTemplateStore.Render(
            "apache-http.conf",
            ("__DOMAIN__", options.Domain),
            ("__SERVER_ALIAS__", BuildScaffoldServerAlias(options)),
            ("__SITE_ID__", options.SiteId));

    private static string BuildScaffoldApacheHttps(PowerForgeServerScaffoldOptions options)
        => ServerScaffoldTemplateStore.Render(
            "apache-https.conf",
            ("__DOMAIN__", options.Domain),
            ("__SERVER_ALIAS__", BuildScaffoldServerAlias(options)),
            ("__SITE_ID__", options.SiteId));

    private static string BuildScaffoldServerAlias(PowerForgeServerScaffoldOptions options)
        => options.IncludeWwwAlias ? $"    ServerAlias www.{options.Domain}" : string.Empty;

    private static string BuildScaffoldOnboarding(PowerForgeServerScaffoldOptions options)
    {
        var cloudflare = options.CloudflareEnabled
            ? "[ ] Create a token restricted to this one zone, store it as the production environment secret `DEPLOYMENT_CLOUDFLARE_API_TOKEN`, and store the exact zone id as the environment variable `CLOUDFLARE_ZONE_ID`."
            : "[ ] Cloudflare is intentionally deferred. When ready, rerun the scaffold in a clean directory with `--cloudflare`, review the diff, and provision a per-site token. Do not reuse an account-wide token.";
        var repository = options.PrivateRepository
            ? $"[ ] Before the first private clone, install the read-only repository key as `/etc/powerforge/repository-ssh/{options.SiteId}_ed25519` and reviewed, pinned GitHub host keys as `/etc/powerforge/repository-ssh/github_known_hosts`; recovery restores these two prerequisites before bootstrap."
            : "[x] The source repository uses public HTTPS recovery; no repository private key is required.";
        return ServerScaffoldTemplateStore.Render(
            "ONBOARDING.md",
            ("__SITE_ID__", options.SiteId),
            ("__BRANCH__", options.Branch),
            ("__HOST__", options.Host),
            ("__SSH_PORT__", options.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("__DOMAIN__", options.Domain),
            ("__REPOSITORY_STEP__", repository),
            ("__CLOUDFLARE_STEP__", cloudflare));
    }
}
