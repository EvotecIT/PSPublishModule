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
        var manifest = files["deploy/linux/example.serverrecovery.json"];

        Assert.DoesNotContain("run: |", workflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-website-deploy.yml@" + EngineRef, workflow, StringComparison.Ordinal);
        Assert.Contains("  id-token: write", workflow, StringComparison.Ordinal);
        Assert.Contains("  pages: write", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_private_key: ${{ secrets.DEPLOYMENT_SSH_PRIVATE_KEY }}", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_ssh_known_hosts: ${{ secrets.DEPLOYMENT_SSH_KNOWN_HOSTS }}", workflow, StringComparison.Ordinal);
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
        Assert.DoesNotContain("\"keepLatest\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"$schema\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"EvotecIT/PSPublishModule/{EngineRef}/Schemas/powerforge.web.serverrecovery.schema.json", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("EvotecIT/PSPublishModule/main/Schemas/powerforge.web.serverrecovery.schema.json", manifest, StringComparison.Ordinal);
        Assert.Contains("restricted SSH accounts", manifest, StringComparison.Ordinal);
        Assert.Equal(2, manifest.Split("\"requiredDuringBootstrap\": false", StringSplitOptions.None).Length - 1);
        Assert.Contains("/usr/local/sbin/powerforge-apache-site-enable --http-site example-test.conf --https-site example-test-le-ssl.conf --certificate-name example.test", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo -n a2ensite", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bootstrap\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("install-static-deployment-runtime", manifest, StringComparison.Ordinal);
        var nextSteps = WebCliCommandHandlers.BuildServerScaffoldNextSteps(options);
        Assert.Contains("authorized-key example files", nextSteps[0], StringComparison.Ordinal);
        Assert.DoesNotContain("host-key", nextSteps[0], StringComparison.Ordinal);

        var manifestNode = JsonNode.Parse(manifest)!;
        var managedPaths = manifestNode["paths"]!.AsArray();
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/usr/local/sbin/powerforge-site-deploy" &&
                                              path["source"]!.GetValue<string>().EndsWith("/powerforge-site-deploy.sh", StringComparison.Ordinal));
        Assert.Contains(managedPaths, path => path!["path"]!.GetValue<string>() == "/etc/powerforge/sites/example.test.env" &&
                                              path["source"]!.GetValue<string>().EndsWith("/example.test.env", StringComparison.Ordinal));
        Assert.Equal(2, managedPaths.Count(path => path!["validation"]?.GetValue<string>() == "sudoers"));
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

        Assert.True(result.IsValid);
        Assert.True(privateResult.IsValid);
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
