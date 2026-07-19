using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerScaffoldTests
{
    private const string EngineRef = "40c8de1dc619329961afad9aef644e00fe4b55f9";

    [Fact]
    public void Scaffold_ShouldGenerateThinSecretFreeCloudflareOffDefaults()
    {
        var options = CreateOptions();

        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(options);
        var workflow = files[".github/workflows/website-deploy.yml"];
        var backupWorkflow = files[".github/workflows/server-backup.yml"];
        var recoveryValidationWorkflow = files[".github/workflows/server-recovery-ci.yml"];
        var manifest = files["deploy/linux/example.serverrecovery.json"];
        var onboarding = files["deploy/linux/ONBOARDING.md"];

        Assert.DoesNotContain("run: |", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-website-deploy.yml@" + EngineRef, workflow, StringComparison.Ordinal);
        Assert.Contains("  id-token: write", workflow, StringComparison.Ordinal);
        Assert.Contains("  pages: write", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_private_key: ${{ secrets.DEPLOYMENT_SSH_PRIVATE_KEY }}", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_known_hosts: ${{ secrets.DEPLOYMENT_SSH_KNOWN_HOSTS }}", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_host: ${{ secrets.DEPLOYMENT_HOST }}", workflow, StringComparison.Ordinal);
        Assert.Contains("server-host: ${{ secrets.DEPLOYMENT_HOST }}", backupWorkflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-server-recovery-validate@" + EngineRef, recoveryValidationWorkflow, StringComparison.Ordinal);
        Assert.Contains("manifest-path: deploy/linux/example.serverrecovery.json", recoveryValidationWorkflow, StringComparison.Ordinal);
        Assert.Contains("capture-user: powerforge-example-backup", recoveryValidationWorkflow, StringComparison.Ordinal);
        Assert.Contains("fail-on-warnings: true", recoveryValidationWorkflow, StringComparison.Ordinal);
        Assert.Contains("- \"Website/deploy/**\"", recoveryValidationWorkflow, StringComparison.Ordinal);
        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(recoveryValidationWorkflow));
        Assert.Equal(1, recoveryValidationWorkflow.Split("uses:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("actions/checkout", recoveryValidationWorkflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("run:", recoveryValidationWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ secrets.", recoveryValidationWorkflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CLOUDFLARE_API_TOKEN", workflow, StringComparison.Ordinal);
        Assert.Contains("CLOUDFLARE_PURGE_ENABLED=0", files["deploy/linux/example.test.env"], StringComparison.Ordinal);
        Assert.DoesNotContain("www.example.test", files["Website/deploy/apache.conf"], StringComparison.Ordinal);
        Assert.DoesNotContain("www.example.test", files["Website/deploy/apache-ssl.conf"], StringComparison.Ordinal);
        Assert.DoesNotContain("www.example.test", manifest, StringComparison.Ordinal);
        Assert.All(files.Where(file => !file.Key.EndsWith(".example", StringComparison.Ordinal)), file =>
        {
            Assert.DoesNotContain("BEGIN OPENSSH PRIVATE KEY", file.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("REPLACE_WITH_", file.Value, StringComparison.Ordinal);
        });
        Assert.Contains("keepLatestInTree", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.10", string.Join('\n', files.Values), StringComparison.Ordinal);
        Assert.Contains("protected environment secret `DEPLOYMENT_HOST`", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("\"keepLatest\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"$schema\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"EvotecIT/PSPublishModule/{EngineRef}/Schemas/powerforge.web.serverrecovery.schema.json", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("EvotecIT/PSPublishModule/main/Schemas/powerforge.web.serverrecovery.schema.json", manifest, StringComparison.Ordinal);
        Assert.Contains("restricted SSH accounts", manifest, StringComparison.Ordinal);
        Assert.Equal(1, manifest.Split("\"requiredDuringBootstrap\": false", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("/etc/letsencrypt/accounts", manifest, StringComparison.Ordinal);
        Assert.Contains("/usr/local/sbin/powerforge-apache-site-enable --http-site example-test.conf --https-site example-test-le-ssl.conf --certificate-name example.test", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo -n a2ensite", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bootstrap\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("install-static-deployment-runtime", manifest, StringComparison.Ordinal);
        var nextSteps = WebCliCommandHandlers.BuildServerScaffoldNextSteps(options);
        Assert.Contains("authorized-key example files", nextSteps[0], StringComparison.Ordinal);
        Assert.DoesNotContain("host-key", nextSteps[0], StringComparison.Ordinal);
        Assert.Contains("DEPLOYMENT_HOST", nextSteps[1], StringComparison.Ordinal);

        var manifestNode = JsonNode.Parse(manifest)!;
        Assert.Equal(2, manifestNode["schemaVersion"]!.GetValue<int>());
        Assert.Null(manifestNode["target"]!["host"]);
        Assert.Null(manifestNode["apache"]!["reloadCommand"]);
        Assert.Equal($"/var/lock/powerforge-site-{options.SiteId}.lock", manifestNode["operationLocks"]![0]!.GetValue<string>());
        Assert.True(manifestNode["apache"]!["sites"]![0]!["enabled"]!.GetValue<bool>());
        Assert.Null(manifestNode["apache"]!["sites"]![1]!["enabled"]);
        Assert.Equal("beforeDeploy", manifestNode["systemd"]!["timers"]![0]!["activation"]!.GetValue<string>());
        Assert.Equal("active", manifestNode["systemd"]!["timers"]![0]!["expectedState"]!.GetValue<string>());
        Assert.DoesNotContain(manifestNode["deploy"]!["commands"]!.AsArray(), command =>
            command!["command"]!.GetValue<string>().Contains("systemctl", StringComparison.Ordinal));
        Assert.Null(manifestNode["certificates"]![0]!["dryRunCommand"]);
        Assert.DoesNotContain(manifestNode["verify"]!["commands"]!.AsArray(), command =>
            command!["id"]!.GetValue<string>().Contains("certbot", StringComparison.Ordinal));
        var managedPaths = manifestNode["paths"]!.AsArray();
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/usr/local/sbin/powerforge-site-deploy" &&
                                              path["source"]!.GetValue<string>().EndsWith("/powerforge-site-deploy.sh", StringComparison.Ordinal));
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/etc/powerforge/sites/example.test.env" &&
                                              path["source"]!.GetValue<string>().EndsWith("/example.test.env", StringComparison.Ordinal));
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/var/lib/powerforge-example-backup" &&
                                              path["owner"]!.GetValue<string>() == "root" &&
                                              path["group"]!.GetValue<string>() == "root" &&
                                              path["mode"]!.GetValue<string>() == "755");
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/var/lib/powerforge-example-backup/.ssh" &&
                                              path["owner"]!.GetValue<string>() == "root" &&
                                              path["group"]!.GetValue<string>() == "root" &&
                                              path["mode"]!.GetValue<string>() == "755");
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/var/lib/powerforge-example-backup/.ssh/authorized_keys" &&
                                              path["owner"]!.GetValue<string>() == "root" &&
                                              path["group"]!.GetValue<string>() == "root" &&
                                              path["mode"]!.GetValue<string>() == "600");
        Assert.Equal(2, managedPaths.Count(path => path!["validation"]?.GetValue<string>() == "sudoers"));
    }

    [Fact]
    public void Scaffold_ShouldRenderYamlSafeDeduplicatedRecoveryWatchPaths()
    {
        var options = CreateOptions();
        options.RecoveryWatchPaths =
        [
            "src/**",
            "deploy/linux/**",
            "src/**",
            ".github/actions/recovery/**",
            "config/__ENGINE_REF__/**",
            "config/__CUSTOM_TOKEN__/**"
        ];

        var workflow = WebCliCommandHandlers.BuildServerScaffoldFiles(options)[".github/workflows/server-recovery-ci.yml"];

        Assert.Equal(1, workflow.Split("      - \"deploy/linux/**\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, workflow.Split("      - \"src/**\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, workflow.Split("      - \".github/actions/recovery/**\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("      - \"config/__ENGINE_REF__/**\"", workflow, StringComparison.Ordinal);
        Assert.Contains("      - \"config/__CUSTOM_TOKEN__/**\"", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-server-recovery-validate@" + EngineRef, workflow, StringComparison.Ordinal);
        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(workflow));
        Assert.DoesNotContain("run:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ secrets.", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scaffold_ShouldSupportRepositoryRootWebsite()
    {
        var options = CreateOptions();
        options.WebsiteRoot = ".";

        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(options);
        var websiteWorkflow = files[".github/workflows/website-deploy.yml"];
        var recoveryWorkflow = files[".github/workflows/server-recovery-ci.yml"];
        var manifest = files["deploy/linux/example.serverrecovery.json"];

        Assert.Contains("deploy/apache.conf", files.Keys);
        Assert.Contains("deploy/apache-ssl.conf", files.Keys);
        Assert.DoesNotContain("./deploy/apache.conf", files.Keys);
        Assert.Contains("      - \"**\"", websiteWorkflow, StringComparison.Ordinal);
        Assert.Contains("      website_root: .", websiteWorkflow, StringComparison.Ordinal);
        Assert.Contains("      pipeline_config: ./pipeline.json", websiteWorkflow, StringComparison.Ordinal);
        Assert.Contains("      - \"deploy/**\"", recoveryWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("./deploy/**", recoveryWorkflow, StringComparison.Ordinal);
        Assert.Contains("/srv/powerforge/sources/example/deploy/apache.conf", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("/./", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldOptions_ShouldNormalizeWebsiteRootSegments()
    {
        var rootOptions = WebCliCommandHandlers.ParseServerScaffoldOptions(
            RequiredArguments("example.test").Concat(["--website-root", "./"]).ToArray());
        var nestedOptions = WebCliCommandHandlers.ParseServerScaffoldOptions(
            RequiredArguments("example.test").Concat(["--website-root", "./Website//./Site/"]).ToArray());

        Assert.Equal(".", rootOptions.WebsiteRoot);
        Assert.Equal("Website/Site", nestedOptions.WebsiteRoot);
    }

    [Fact]
    public void Scaffold_ShouldDeriveBackupSudoersAndRestrictedKeyExamplesFromManifest()
    {
        var options = CreateOptions();
        options.PrivateRepository = true;

        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(options);
        var sudoers = files["deploy/linux/powerforge-example-backup.sudoers"];
        var deploymentKey = files["deploy/linux/powerforge-example-authorized_keys.example"];
        var backupKey = files["deploy/linux/powerforge-example-backup-authorized_keys.example"];

        Assert.StartsWith("restrict ssh-ed25519", deploymentKey, StringComparison.Ordinal);
        Assert.StartsWith("restrict ssh-ed25519", backupKey, StringComparison.Ordinal);
        Assert.Contains("/etc/powerforge/repository-ssh/example_ed25519", sudoers, StringComparison.Ordinal);
        Assert.Contains("Cmnd_Alias PF_EXAMPLE_BACKUP_ENCRYPTED = /usr/local/sbin/powerforge-server-encrypted-capture", sudoers, StringComparison.Ordinal);
        Assert.Contains("--recipient age1examplepublicrecipient", sudoers, StringComparison.Ordinal);
        Assert.DoesNotContain("BACKUP_ENCRYPTED = /usr/bin/tar", sudoers, StringComparison.Ordinal);
        var manifest = files["deploy/linux/example.serverrecovery.json"];
        Assert.Contains("\"host\": \"192.0.2.10\"", manifest, StringComparison.Ordinal);
        Assert.Contains("example-repository-private-key", manifest, StringComparison.Ordinal);
        Assert.Contains("git@github.com:ExampleOrg/ExampleSite.git", manifest, StringComparison.Ordinal);
        Assert.Contains("\"sshIdentityFile\": \"/etc/powerforge/repository-ssh/example_ed25519\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"sshKnownHostsFile\": \"/etc/powerforge/repository-ssh/github_known_hosts\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"refCaptureCommandId\": \"static-source-ref\"", manifest, StringComparison.Ordinal);
        var manifestNode = JsonNode.Parse(manifest)!;
        Assert.Equal(EngineRef, manifestNode["repositories"]![0]!["ref"]!.GetValue<string>());
        Assert.Contains("deployment.json", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("{40,64}", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com-example", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(files.Keys, key => key.EndsWith("repository-ssh.conf", StringComparison.Ordinal));
        Assert.DoesNotContain("$(ssh-keyscan", string.Join('\n', files.Values), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"source\": \"/srv/powerforge/sources/example/deploy/linux/github_known_hosts\"", manifest, StringComparison.Ordinal);
        Assert.Contains("deploy/linux/github_known_hosts.example", files.Keys);
        Assert.DoesNotContain("deploy/linux/github_known_hosts", files.Keys);
        var onboarding = files["deploy/linux/ONBOARDING.md"];
        Assert.Contains("remove the `.example` suffix", onboarding, StringComparison.Ordinal);
        Assert.Contains("commit the resulting public host-key file", onboarding, StringComparison.Ordinal);
        Assert.Contains("the committed `deploy/linux/github_known_hosts`", onboarding, StringComparison.Ordinal);
        Assert.Contains("authorized-key and reviewed host-key example files", WebCliCommandHandlers.BuildServerScaffoldNextSteps(options)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_ShouldOnlyWireCloudflareWhenExplicitlyRequested()
    {
        var options = CreateOptions();
        options.CloudflareEnabled = true;

        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(options);
        var workflow = files[".github/workflows/website-deploy.yml"];

        Assert.Contains("deployment_cloudflare_zone: example.test", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_cloudflare_api_token: ${{ secrets.DEPLOYMENT_CLOUDFLARE_API_TOKEN }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.CLOUDFLARE_API_TOKEN", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("CLOUDFLARE_ZONE_ID", workflow, StringComparison.Ordinal);
        Assert.Contains("CLOUDFLARE_PURGE_ENABLED=1", files["deploy/linux/example.test.env"], StringComparison.Ordinal);
        Assert.Contains("token restricted to this one zone", files["deploy/linux/ONBOARDING.md"], StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_ShouldOnlyAddWwwAliasWhenExplicitlyRequested()
    {
        var options = CreateOptions();
        options.IncludeWwwAlias = true;

        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(options);
        var manifest = files["deploy/linux/example.serverrecovery.json"];

        Assert.Contains("ServerAlias www.example.test", files["Website/deploy/apache.conf"], StringComparison.Ordinal);
        Assert.Contains("ServerAlias www.example.test", files["Website/deploy/apache-ssl.conf"], StringComparison.Ordinal);
        Assert.Contains("www.example.test", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_ShouldCaptureOneExactAcmeAccountWhenConfigured()
    {
        var options = CreateOptions();
        options.AcmeAccountId = "0123456789abcdef";

        var manifest = JsonNode.Parse(
            WebCliCommandHandlers.BuildServerScaffoldFiles(options)["deploy/linux/example.serverrecovery.json"])!;
        const string accountPath = "/etc/letsencrypt/accounts/acme-v02.api.letsencrypt.org/directory/0123456789abcdef";

        Assert.Contains(manifest["capture"]!["encryptedFiles"]!.AsArray(), file =>
            file!["target"]!.GetValue<string>() == accountPath);
        Assert.Contains(manifest["secrets"]!.AsArray(), secret =>
            secret!["path"]!.GetValue<string>() == accountPath &&
            secret["capture"]!.GetValue<string>() == "encrypted");
        Assert.Contains("certbot renew --dry-run", manifest["certificates"]![0]!["dryRunCommand"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(manifest["verify"]!["commands"]!.AsArray(), command =>
            command!["id"]!.GetValue<string>().Contains("certbot", StringComparison.Ordinal));

        var schema = JsonSchema.FromText(File.ReadAllText(GetRepoPath("Schemas", "powerforge.web.serverrecovery.schema.json")));
        Assert.True(EvaluateSchema(schema, manifest));
    }

    [Fact]
    public void Scaffold_ManifestShouldSatisfyPublishedSchema()
    {
        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(CreateOptions());
        var privateOptions = CreateOptions();
        privateOptions.PrivateRepository = true;
        var privateFiles = WebCliCommandHandlers.BuildServerScaffoldFiles(privateOptions);
        var schema = JsonSchema.FromText(File.ReadAllText(GetRepoPath("Schemas", "powerforge.web.serverrecovery.schema.json")));
        var manifest = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!;
        var privateManifest = JsonNode.Parse(privateFiles["deploy/linux/example.serverrecovery.json"])!;

        var result = schema.Evaluate(manifest, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var privateResult = schema.Evaluate(privateManifest, new EvaluationOptions { OutputFormat = OutputFormat.List });

        var numericIdentityManifest = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        numericIdentityManifest["paths"]![0]!["owner"] = "0";
        numericIdentityManifest["paths"]![0]!["group"] = "65534";
        var numericIdentityResult = schema.Evaluate(numericIdentityManifest, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid);
        Assert.True(privateResult.IsValid);
        Assert.True(numericIdentityResult.IsValid);
    }

    [Fact]
    public void PublishedSchema_ShouldMatchRuntimeCaptureAndBackupContracts()
    {
        var files = WebCliCommandHandlers.BuildServerScaffoldFiles(CreateOptions());
        var schema = JsonSchema.FromText(File.ReadAllText(GetRepoPath("Schemas", "powerforge.web.serverrecovery.schema.json")));

        var missingSensitivity = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        missingSensitivity["capture"]!["encryptedFiles"]![0]!.AsObject().Remove("sensitive");
        Assert.False(EvaluateSchema(schema, missingSensitivity));

        var incompleteBackup = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        incompleteBackup["backupTarget"]!.AsObject().Remove("repository");
        Assert.False(EvaluateSchema(schema, incompleteBackup));

        var emptyCapture = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        emptyCapture["capture"]!["plainFiles"]!.AsArray().Clear();
        Assert.False(EvaluateSchema(schema, emptyCapture));

        var unsafeBackupLocation = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        unsafeBackupLocation["backupTarget"]!["branch"] = "../main";
        unsafeBackupLocation["backupTarget"]!["path"] = "/absolute";
        Assert.False(EvaluateSchema(schema, unsafeBackupLocation));

        var newlineBackupLocation = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        newlineBackupLocation["backupTarget"]!["branch"] = "main\n";
        Assert.False(EvaluateSchema(schema, newlineBackupLocation));

        var unsupportedRetention = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        unsupportedRetention["backupTarget"]!["retention"]!["keepDays"] = 30;
        Assert.False(EvaluateSchema(schema, unsupportedRetention));

        var privateOptions = CreateOptions();
        privateOptions.PrivateRepository = true;
        var privateFiles = WebCliCommandHandlers.BuildServerScaffoldFiles(privateOptions);
        var incompleteRepositorySsh = JsonNode.Parse(privateFiles["deploy/linux/example.serverrecovery.json"])!.AsObject();
        incompleteRepositorySsh["repositories"]![0]!.AsObject().Remove("sshKnownHostsFile");
        Assert.False(EvaluateSchema(schema, incompleteRepositorySsh));

        var incompleteSudoers = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var sudoersPath = incompleteSudoers["paths"]!.AsArray()
            .First(path => path!["validation"]?.GetValue<string>() == "sudoers")!
            .AsObject();
        sudoersPath.Remove("kind");
        Assert.False(EvaluateSchema(schema, incompleteSudoers));

        var untaggedSudoers = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var untaggedSudoersPath = untaggedSudoers["paths"]!.AsArray()
            .First(path => path!["validation"]?.GetValue<string>() == "sudoers")!
            .AsObject();
        untaggedSudoersPath.Remove("validation");
        Assert.False(EvaluateSchema(schema, untaggedSudoers));

        var apacheSudoersTarget = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        apacheSudoersTarget["apache"]!["sites"]![0]!["target"] = "/etc/sudoers.d/powerforge-apache";
        Assert.False(EvaluateSchema(schema, apacheSudoersTarget));

        var systemdSudoersTarget = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        systemdSudoersTarget["systemd"]!["services"]![0]!["target"] = "/etc/sudoers";
        Assert.False(EvaluateSchema(schema, systemdSudoersTarget));

        var impossibleRuntime = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        impossibleRuntime["packages"]!["dotnetSdks"] = new JsonArray("8.1");
        Assert.False(EvaluateSchema(schema, impossibleRuntime));

        var legacyRuntime = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        legacyRuntime["packages"]!["dotnetSdks"] = new JsonArray("1", "2", "3", "2.1", "2.2", "3.1");
        Assert.False(EvaluateSchema(schema, legacyRuntime));

        var supportedRuntime = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        supportedRuntime["packages"]!["dotnetSdks"] = new JsonArray("8", "8.0", "10", "10.0");
        Assert.True(EvaluateSchema(schema, supportedRuntime));

        var uppercaseAptPackage = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        uppercaseAptPackage["packages"]!["apt"] = new JsonArray("Curl");
        Assert.False(EvaluateSchema(schema, uppercaseAptPackage));

        var legacySchema = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        legacySchema["schemaVersion"] = 1;
        Assert.False(EvaluateSchema(schema, legacySchema));

        var trailingManagedSource = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var sourceManagedPath = trailingManagedSource["paths"]!.AsArray()
            .First(path => path?["source"] is not null)!;
        sourceManagedPath["source"] = sourceManagedPath["source"]!.GetValue<string>() + "/";
        Assert.False(EvaluateSchema(schema, trailingManagedSource));

        var trailingManagedDirectory = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var managedDirectory = trailingManagedDirectory["paths"]!.AsArray()
            .First(path => path?["kind"]?.GetValue<string>() == "directory")!;
        managedDirectory["path"] = managedDirectory["path"]!.GetValue<string>() + "/";
        Assert.False(EvaluateSchema(schema, trailingManagedDirectory));

        var trailingRepository = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var repositoryPath = trailingRepository["repositories"]!.AsArray()[0]!["path"]!;
        trailingRepository["repositories"]!.AsArray()[0]!["path"] = repositoryPath.GetValue<string>() + "/";
        Assert.False(EvaluateSchema(schema, trailingRepository));

        var trailingApacheTarget = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var apacheSite = trailingApacheTarget["apache"]!["sites"]!.AsArray()[0]!;
        apacheSite["target"] = apacheSite["target"]!.GetValue<string>() + "/";
        Assert.False(EvaluateSchema(schema, trailingApacheTarget));

        var invalidOperationLock = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        invalidOperationLock["operationLocks"] = new JsonArray("/tmp/powerforge-site-example.lock");
        Assert.False(EvaluateSchema(schema, invalidOperationLock));

        var maximumOperationLock = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        maximumOperationLock["operationLocks"] = new JsonArray($"/var/lock/{new string('a', 126)}.lock");
        Assert.True(EvaluateSchema(schema, maximumOperationLock));

        var oversizedOperationLock = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        oversizedOperationLock["operationLocks"] = new JsonArray($"/var/lock/{new string('a', 127)}.lock");
        Assert.False(EvaluateSchema(schema, oversizedOperationLock));

        var duplicateOperationLock = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        duplicateOperationLock["operationLocks"] = new JsonArray(
            "/var/lock/powerforge-site-example.lock",
            "/var/lock/powerforge-site-example.lock");
        Assert.False(EvaluateSchema(schema, duplicateOperationLock));

        var apacheActivation = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        apacheActivation["apache"]!["sites"]![0]!["enabled"] = true;
        Assert.True(EvaluateSchema(schema, apacheActivation));
        apacheActivation["apache"]!["sites"]![0]!["enabled"] = "yes";
        Assert.False(EvaluateSchema(schema, apacheActivation));

        var systemdActivation = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var systemdTimer = systemdActivation["systemd"]!["timers"]![0]!;
        systemdTimer["enabled"] = false;
        Assert.False(EvaluateSchema(schema, systemdActivation));

        var systemdExpectedState = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        var stateTimer = systemdExpectedState["systemd"]!["timers"]![0]!;
        stateTimer.AsObject().Remove("activation");
        Assert.False(EvaluateSchema(schema, systemdExpectedState));

        var invalidSystemdName = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        invalidSystemdName["systemd"]!["timers"]![0]!["name"] = "--help.timer";
        Assert.False(EvaluateSchema(schema, invalidSystemdName));

        var serviceWithTimerSuffix = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        serviceWithTimerSuffix["systemd"]!["services"]![0]!["name"] = "cleanup.timer";
        Assert.False(EvaluateSchema(schema, serviceWithTimerSuffix));

        var timerWithServiceSuffix = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        timerWithServiceSuffix["systemd"]!["timers"]![0]!["name"] = "cleanup.service";
        Assert.False(EvaluateSchema(schema, timerWithServiceSuffix));

        var whitespaceCommand = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        whitespaceCommand["deploy"]!["commands"]![0]!["command"] = "   ";
        Assert.False(EvaluateSchema(schema, whitespaceCommand));

        var oversizedApacheName = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        oversizedApacheName["apache"]!["sites"]![0]!["target"] =
            "/etc/apache2/sites-available/" + new string('a', 251) + ".conf";
        Assert.False(EvaluateSchema(schema, oversizedApacheName));

        var deferredSecret = JsonNode.Parse(files["deploy/linux/example.serverrecovery.json"])!.AsObject();
        deferredSecret["secrets"]![0]!["restoreAfterRepositories"] = true;
        Assert.True(EvaluateSchema(schema, deferredSecret));
        deferredSecret["secrets"]![0]!["restoreAfterRepositories"] = "yes";
        Assert.False(EvaluateSchema(schema, deferredSecret));
    }

    [Fact]
    public void ScaffoldOptions_ShouldDeriveBoundedStableSiteIdAndRejectNonExactEngineRef()
    {
        var args = RequiredArguments("domain-detective-website.example");

        var options = WebCliCommandHandlers.ParseServerScaffoldOptions(args);

        Assert.Matches("^[a-z][a-z0-9-]{0,13}$", options.SiteId);
        Assert.Equal(options.SiteId, WebCliCommandHandlers.ParseServerScaffoldOptions(args).SiteId);

        var sameLabelOtherDomain = WebCliCommandHandlers.ParseServerScaffoldOptions(RequiredArguments("domain-detective-website.test"));
        Assert.NotEqual(options.SiteId, sameLabelOtherDomain.SiteId);

        var numericLabel = WebCliCommandHandlers.ParseServerScaffoldOptions(RequiredArguments("123.example"));
        Assert.Matches("^[a-z][a-z0-9-]{0,13}$", numericLabel.SiteId);

        args[5] = "main";
        var exception = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(args));
        Assert.Contains("exact 40-character commit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldOptions_ShouldRejectNumericSiteIdAndDuplicateWwwPrefix()
    {
        var numericSiteId = RequiredArguments("example.test").Concat(["--site-id", "123example"]).ToArray();
        var siteIdException = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(numericSiteId));
        Assert.Contains("start with a letter", siteIdException.Message, StringComparison.Ordinal);

        var duplicateWww = RequiredArguments("www.example.test").Concat(["--www"]).ToArray();
        var wwwException = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(duplicateWww));
        Assert.Contains("already starts with www", wwwException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldOptions_ShouldReadRepeatedAndListRecoveryWatchPaths()
    {
        var args = RequiredArguments("example.test").Concat(
        [
            "--recovery-watch-path", "src/**,Docs/**",
            "--recovery-watch-path", "src/**",
            "--recovery-watch-paths", ".github/actions/**;tests/?/fixtures/**"
        ]).ToArray();

        var options = WebCliCommandHandlers.ParseServerScaffoldOptions(args);

        Assert.Equal(["src/**", "Docs/**", ".github/actions/**", "tests/?/fixtures/**"], options.RecoveryWatchPaths);
    }

    [Theory]
    [InlineData("/absolute/**")]
    [InlineData("../secrets/**")]
    [InlineData("src/../../secrets/**")]
    [InlineData("src\\**")]
    [InlineData("src//**")]
    [InlineData("src/**\n      run: whoami")]
    [InlineData("!src/**")]
    public void ScaffoldOptions_ShouldRejectUnsafeRecoveryWatchPathGlobs(string watchPath)
    {
        var args = RequiredArguments("example.test")
            .Concat(["--recovery-watch-path", watchPath])
            .ToArray();

        var exception = Assert.Throws<InvalidOperationException>(
            () => WebCliCommandHandlers.ParseServerScaffoldOptions(args));

        Assert.Contains("safe repository-relative positive glob", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--recovery-watch-path")]
    [InlineData("--recovery-watch-paths")]
    public void ScaffoldOptions_ShouldRejectRecoveryWatchPathWithoutValue(string optionName)
    {
        var trailingOption = RequiredArguments("example.test").Append(optionName).ToArray();
        var followedByOption = RequiredArguments("example.test")
            .Concat([optionName, "--private-repository"])
            .ToArray();

        var trailingException = Assert.Throws<InvalidOperationException>(
            () => WebCliCommandHandlers.ParseServerScaffoldOptions(trailingOption));
        var followedException = Assert.Throws<InvalidOperationException>(
            () => WebCliCommandHandlers.ParseServerScaffoldOptions(followedByOption));

        Assert.Contains("requires a glob value", trailingException.Message, StringComparison.Ordinal);
        Assert.Contains("requires a glob value", followedException.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(" ; , ")]
    public void ScaffoldOptions_ShouldRejectEmptyRecoveryWatchPathList(string watchPaths)
    {
        var args = RequiredArguments("example.test")
            .Concat(["--recovery-watch-path", watchPaths])
            .ToArray();

        var exception = Assert.Throws<InvalidOperationException>(
            () => WebCliCommandHandlers.ParseServerScaffoldOptions(args));

        Assert.Contains("requires at least one glob value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldOptions_ShouldFitGeneratedApacheSiteFilename()
    {
        var prefix = string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63));
        var longestSupportedDomain = $"{prefix}.{new string('d', 51)}";
        var oversizedDomain = $"{prefix}.{new string('d', 52)}";

        Assert.Equal(243, longestSupportedDomain.Length);
        Assert.Equal(longestSupportedDomain, WebCliCommandHandlers.ParseServerScaffoldOptions(RequiredArguments(longestSupportedDomain)).Domain);

        var exception = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(RequiredArguments(oversizedDomain)));
        Assert.Contains("Apache site filename", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/$(touch-pwned)")]
    [InlineData("/`touch-pwned`")]
    [InlineData("/\"\nINJECTED=1\n#")]
    [InlineData("/path\\escape")]
    public void ScaffoldOptions_ShouldRejectSmokePathsThatCanEscapeGeneratedFormats(string smokePaths)
    {
        var args = RequiredArguments("example.test").Concat(["--smoke-paths", smokePaths]).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(args));

        Assert.Contains("URL-safe characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldOptions_ShouldRejectIpv6UntilDeploymentSupportsIt()
    {
        var args = RequiredArguments("example.test");
        args[7] = "2001:db8::1";

        var exception = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(args));

        Assert.Contains("DNS name or IPv4 address", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldOptions_ShouldValidateExactAcmeAccountDirectoryName()
    {
        var valid = RequiredArguments("example.test").Concat(["--acme-account-id", "0123456789abcdef"]).ToArray();
        var invalid = RequiredArguments("example.test").Concat(["--acme-account-id", "../account"]).ToArray();

        Assert.Equal("0123456789abcdef", WebCliCommandHandlers.ParseServerScaffoldOptions(valid).AcmeAccountId);
        var exception = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.ParseServerScaffoldOptions(invalid));
        Assert.Contains("exact Certbot account directory", exception.Message, StringComparison.Ordinal);
    }

    private static PowerForgeServerScaffoldOptions CreateOptions()
        => new()
        {
            Domain = "example.test",
            SiteId = "example",
            Repository = "ExampleOrg/ExampleSite",
            RepositoryRef = EngineRef,
            Branch = "main",
            WebsiteRoot = "Website",
            EngineRef = EngineRef,
            Host = "192.0.2.10",
            SshPort = 22222,
            BackupRepository = "ExampleOrg/ServerBackups",
            BackupRecipient = "age1examplepublicrecipient",
            SmokePaths = "/ /sitemap.xml",
            OutputRoot = "."
        };

    private static string[] RequiredArguments(string domain)
        =>
        [
            "--domain", domain,
            "--repository", "ExampleOrg/ExampleSite",
            "--engine-ref", EngineRef,
            "--host", "192.0.2.10",
            "--backup-repository", "ExampleOrg/ServerBackups",
            "--backup-recipient", "age1examplepublicrecipient",
            "--repository-ref", EngineRef
        ];

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

    private static bool EvaluateSchema(JsonSchema schema, JsonNode node)
        => schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid;
}
