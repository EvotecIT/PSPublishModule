namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    private const string CaptureUser = "powerforge-example-backup";
    private const string CallerRepository = "EvotecIT/ExampleSite";
    private const string EngineRepository = "EvotecIT/PSPublishModule";
    private const string EngineRef = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Recipient = "age1example";
    private const string ExpectedPlainCaptureCommand = "/usr/bin/tar -czf - /etc/example/config";
    private const string ExpectedCaptureCommand = "/usr/local/sbin/powerforge-server-encrypted-capture --recipient age1example -- /etc/example/secret";
    private const string ExpectedInspectCommand = "/usr/sbin/apachectl -S";
    private const string RestrictedCaptureKey = "restrict ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPowerForgeRecoveryFixtureKey example";

    [Theory]
    [InlineData("https://github.com/EvotecIT/ExampleSite.git")]
    [InlineData("ssh://git@github.com/EvotecIT/ExampleSite.git")]
    [InlineData("git@github.com:EvotecIT/ExampleSite.git")]
    public void Validator_ShouldAcceptExplicitGitHubRepositoryForms(string repositoryUrl)
    {
        var result = RunValidator(repositoryUrl: repositoryUrl);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldPartitionMixedOptionalEncryptedCapture()
    {
        var result = RunValidator(includeOptionalEncryptedCapture: true);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldResolveEngineAndCallerSourcesWhenRepositorySlugsMatch()
    {
        var result = RunValidator(
            repositoryUrl: "https://github.com/EvotecIT/PSPublishModule.git",
            callerRepository: EngineRepository);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData("https://evilgithub.com/EvotecIT/ExampleSite.git")]
    [InlineData("git@github.com-evil.com:EvotecIT/ExampleSite.git")]
    [InlineData("git@github.com-example:EvotecIT/ExampleSite.git")]
    public void Validator_ShouldRejectLookalikeGitHubHosts(string repositoryUrl)
    {
        var result = RunValidator(repositoryUrl: repositoryUrl);

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
        Assert.True(
            result.AllOutput.Contains("must authorize exactly one command", StringComparison.Ordinal),
            result.AllOutput);
    }

    [Fact]
    public void Validator_ShouldIgnoreBroaderAliasesNotGrantedToCaptureUser()
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      $"Cmnd_Alias DEPLOY_BROAD = {ExpectedCaptureCommand}, /usr/bin/tar -czf - /etc/example/secret\n" +
                      "deployment-user ALL=(root) NOPASSWD: DEPLOY_BROAD\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData("other-user", "root")]
    [InlineData(CaptureUser, "backup")]
    public void Validator_ShouldBindAuthorizationToCaptureUserAndRoot(string principal, string runAs)
    {
        var sudoers = BuildExpectedSudoers(principal, runAs);

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            principal == CaptureUser ? "only as root" : "do not authorize the exact hardened encrypted-capture command",
            result.AllOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectAdditionalCaptureAccountGrants()
    {
        var sudoers = $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_INSPECT = {ExpectedInspectCommand}\n" +
                      "Cmnd_Alias BACKUP_DIRECT_TAR = /usr/bin/tar -czf - /etc/example/secret\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT, BACKUP_DIRECT_TAR\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectAlternateHostCaptureAccountGrants()
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      $"{CaptureUser} localhost=(root) NOPASSWD: BACKUP_ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must use the exact ALL=(root) form", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectInvalidSudoersAliasNames()
    {
        var sudoers = $"Cmnd_Alias BACKUP-ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP-ENCRYPTED\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("invalid command alias", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldIgnoreCloneOnlyNonGitHubRepositories()
    {
        var result = RunValidator(includeCloneOnlyRepository: true);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldIgnoreCloneOnlyRepositoriesWithoutUrls()
    {
        var result = RunValidator(includeCloneOnlyRepositoryWithoutUrl: true);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectSymlinkModeInPinnedSources()
    {
        var result = RunValidator(pinSudoersAsSymlink: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unsupported Git mode 120000", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectWildcardedPlainCapturePaths()
    {
        var result = RunValidator(plainCaptureTarget: "/etc/example/*.conf");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Plain recovery capture path contains unsupported characters", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireRootOwnedHelperMetadata()
    {
        var result = RunValidator(helperOwner: CaptureUser);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact managed helper from the pinned PowerForge engine", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("755")]
    [InlineData("0755")]
    public void Validator_ShouldAcceptSchemaEquivalentHelperModes(string mode)
    {
        var result = RunValidator(helperMode: mode);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRequireRootOwnedSudoersMetadata()
    {
        var result = RunValidator(sudoersOwner: CaptureUser);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must be root-owned files with mode 440 or 0440", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("440")]
    [InlineData("0440")]
    public void Validator_ShouldAcceptSchemaSupportedSudoersModes(string mode)
    {
        var result = RunValidator(sudoersMode: mode);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectSchemaInvalidSudoersModes()
    {
        var result = RunValidator(sudoersMode: "400");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must be root-owned files with mode 440 or 0440", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectUntaggedSudoersTargets()
    {
        var result = RunValidator(tagSudoers: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must declare sudoers validation", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/sudoers", "must not replace /etc/sudoers")]
    [InlineData("/etc/sudoers.d", "must not replace /etc/sudoers")]
    [InlineData("/etc/sudoers.d/powerforge-apache", "must declare sudoers validation")]
    public void Validator_ShouldRejectSudoersTargetsFromAlternateManifestSections(string target, string expectedError)
    {
        var result = RunValidator(alternateManagedSudoersTarget: target);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedError, result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#1001 ALL=(root) NOPASSWD: /bin/sh")]
    [InlineData("#1001,other-user ALL=(root) NOPASSWD: /bin/sh")]
    public void Validator_ShouldRejectNumericUidPrincipals(string numericGrant)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      numericGrant + "\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not use numeric user principals", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldInspectEveryManagedSudoersSource()
    {
        var extraSudoers = "Cmnd_Alias BACKUP_DIRECT = /usr/bin/tar -czf - /etc/example/secret\n" +
                           $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_DIRECT\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldAllowUnrelatedMultiCommandAliases()
    {
        var extraSudoers = "Cmnd_Alias DEPLOY_TOOLS = /usr/bin/true, /usr/bin/false\n" +
                           "deployment-user ALL=(root) NOPASSWD: DEPLOY_TOOLS\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("4", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldResolveCaptureAliasesAcrossManagedSudoersSources()
    {
        var grant = $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT\n";

        var result = RunValidator(sudoers: BuildExpectedAliases(), additionalSudoers: grant);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("4", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldTreatMissingCaptureConfigurationAsEmpty()
    {
        var result = RunValidator(includeCapture: false);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectSudoersIncludesWithoutEncryptedCapture()
    {
        var result = RunValidator(
            sudoers: "@includedir /etc/sudoers.d\n",
            includeCapture: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not include additional policy files", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectCaptureGrantsWithoutEncryptedCapture()
    {
        var result = RunValidator(
            sudoers: BuildExpectedSudoers(CaptureUser, "root"),
            includeCapture: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("when encrypted capture is not configured", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectUserAliasesInManagedSudoers()
    {
        var extraSudoers = $"User_Alias PF_BACKUP = {CaptureUser}\n" +
                           "Cmnd_Alias BACKUP_DIRECT = /bin/sh\n" +
                           "PF_BACKUP ALL=(root) NOPASSWD: BACKUP_DIRECT\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not use User_Alias entries", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldCompareGrantedCommandsCaseSensitively()
    {
        var sudoers = $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand.Replace("/usr/bin/tar", "/USR/BIN/TAR", StringComparison.Ordinal)}\n" +
                      $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_INSPECT = {ExpectedInspectCommand}\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED, BACKUP_INSPECT\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not authorize the exact hardened encrypted-capture command", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectLowercaseCommandAliases()
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root")
            .Replace("BACKUP_INSPECT", "backup_inspect", StringComparison.Ordinal);

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("invalid command alias", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectUppercaseAgeRecipients()
    {
        var result = RunValidator(recipient: "AGE1EXAMPLE");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("stable age public recipient", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireInlineRecipientForCredentialFreeValidation()
    {
        var result = RunValidator(recipient: "", recipientEnv: "POWERFORGE_AGE_RECIPIENT");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not resolve backupTarget.recipientEnv", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldNormalizePrivilegedCaptureCommandWhitespace()
    {
        var result = RunValidator(captureCommand: "  sudo -n apachectl -S  ");

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData("Defaults !authenticate")]
    [InlineData($"Defaults:{CaptureUser} !authenticate")]
    [InlineData($"Defaults: {CaptureUser} !authenticate")]
    [InlineData($"Defaults exempt_group={CaptureUser}")]
    [InlineData($"Defaults exempt_group = {CaptureUser}")]
    [InlineData($"Defaults: {CaptureUser} exempt_group={CaptureUser}")]
    public void Validator_ShouldRejectAuthenticationDisablingDefaults(string defaults)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      defaults + "\n" +
                      $"{CaptureUser} ALL=(root) /bin/sh\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not disable authentication", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectNonCanonicalSudoCapturePrefixes()
    {
        var result = RunValidator(captureCommand: "SUDO -n apachectl -S");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("canonical case-sensitive sudo -n prefix", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test -x /usr/sbin/apachectl && sudo -n /usr/sbin/apachectl -S")]
    [InlineData("/usr/bin/sudo -n /usr/sbin/apachectl -S")]
    [InlineData("sudo</dev/null -n /usr/sbin/apachectl -S")]
    [InlineData("s\\udo -n /usr/sbin/apachectl -S")]
    [InlineData("su''do -n /usr/sbin/apachectl -S")]
    [InlineData("su\"\"do -n /usr/sbin/apachectl -S")]
    [InlineData("s\\\nudo -n /usr/sbin/apachectl -S")]
    public void Validator_ShouldRejectEmbeddedSudoCaptureCommands(string command)
    {
        var result = RunValidator(captureCommand: command);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("canonical case-sensitive sudo -n prefix", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$SUDO -n /usr/sbin/apachectl -S")]
    [InlineData("s${UNSET}udo -n /usr/sbin/apachectl -S")]
    [InlineData("$(printf sudo) -n /usr/sbin/apachectl -S")]
    [InlineData("`printf sudo` -n /usr/sbin/apachectl -S")]
    public void Validator_ShouldRejectDynamicShellExpansionInCaptureCommands(string command)
    {
        var result = RunValidator(captureCommand: command);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unsupported dynamic shell expansion", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dpkg-query -W -f='${binary:Package}\\t${Version}\\n'")]
    [InlineData("systemctl list-units 'evotec-*' --all --no-pager")]
    [InlineData("systemctl list-units \"evotec-*\" --all --no-pager")]
    public void Validator_ShouldAllowExpansionSyntaxInsideQuotedArguments(string command)
    {
        var sudoers = $"Cmnd_Alias BACKUP_PLAIN = {ExpectedPlainCaptureCommand}\n" +
                      $"Cmnd_Alias BACKUP_ENCRYPTED = {ExpectedCaptureCommand}\n" +
                      $"{CaptureUser} ALL=(root) NOPASSWD: BACKUP_PLAIN, BACKUP_ENCRYPTED\n";
        var result = RunValidator(
            sudoers: sudoers,
            captureCommand: command);

        Assert.True(result.ExitCode == 0, result.AllOutput);
    }

    [Theory]
    [InlineData("/usr/bin/s[u]do -n /usr/sbin/apachectl -S")]
    [InlineData("/usr/bin/s?do -n /usr/sbin/apachectl -S")]
    [InlineData("/usr/bin/s*do -n /usr/sbin/apachectl -S")]
    [InlineData("/usr/bin/s{u,}do -n /usr/sbin/apachectl -S")]
    public void Validator_ShouldRejectUnquotedShellExpansionInCaptureCommands(string command)
    {
        var result = RunValidator(captureCommand: command);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unsupported pathname or brace shell expansion", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sudo -n /usr/bin/sudo -n /usr/sbin/apachectl -S")]
    [InlineData("sudo -n /bin/sh -c sudo")]
    [InlineData("sudo -n /bin/sh -c sudo</dev/null")]
    public void Validator_ShouldRejectNestedSudoCaptureCommands(string command)
    {
        var result = RunValidator(captureCommand: command);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly one canonical sudo -n prefix", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sudo -n /bin/sh")]
    [InlineData("sudo -n /usr/bin/true")]
    public void Validator_ShouldRejectPrivilegedCommandsWithoutFixedArguments(string command)
    {
        var result = RunValidator(captureCommand: command);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must include fixed arguments", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sudo -n /usr/bin/chmod 0666 /etc/shadow")]
    [InlineData("sudo -n /usr/bin/cat /etc/shadow")]
    public void Validator_ShouldRejectPrivilegedCommandsOutsideReadOnlyAllowlist(string command)
    {
        var result = RunValidator(captureCommand: command);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("approved read-only command set", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sudo -n ufw status numbered")]
    [InlineData("sudo -n /usr/sbin/ufw status numbered")]
    public void Validator_ShouldAllowApprovedFirewallStatusCapture(string command)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root")
            .Replace(ExpectedInspectCommand, "/usr/sbin/ufw status numbered", StringComparison.Ordinal);

        var result = RunValidator(sudoers: sudoers, captureCommand: command);

        Assert.True(result.ExitCode == 0, result.AllOutput);
    }

    [Fact]
    public void Validator_ShouldRejectSudoersThatFailVisudo()
    {
        var result = RunValidator(
            additionalSudoers: "this is not valid sudoers\n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("failed visudo syntax validation", result.AllOutput, StringComparison.Ordinal);
        Assert.Equal(2, result.VisudoInvocations.Length);
        Assert.DoesNotContain("VISUDO_STDOUT_SENTINEL", result.AllOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("VISUDO_STDERR_SENTINEL", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRunVisudoForEveryManagedSourceWithExactArguments()
    {
        var additionalSudoers = "Cmnd_Alias DEPLOY_TOOLS = /usr/bin/true, /usr/bin/false\n" +
                                "deployment-user ALL=(root) NOPASSWD: DEPLOY_TOOLS\n";

        var result = RunValidator(additionalSudoers: additionalSudoers);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal(3, result.VisudoInvocations.Length);
        Assert.Contains(result.VisudoInvocations, path => path.EndsWith("backup.sudoers", StringComparison.Ordinal));
        Assert.Contains(result.VisudoInvocations, path => path.EndsWith("extra.sudoers", StringComparison.Ordinal));
        Assert.Contains(result.VisudoInvocations, path => Path.GetFileName(path).StartsWith("powerforge-managed-sudoers-", StringComparison.Ordinal));
    }

    [Fact]
    public void LinuxValidator_ShouldUseRealVisudoBoundary()
    {
        if (!OperatingSystem.IsLinux())
            return;

        Assert.True(File.Exists("/usr/sbin/visudo"));
        var valid = RunValidator(visudoPathOverride: "/usr/sbin/visudo");
        var invalid = RunValidator(
            additionalSudoers: "this is not valid sudoers\n",
            visudoPathOverride: "/usr/sbin/visudo");

        Assert.True(valid.ExitCode == 0, valid.AllOutput);
        Assert.NotEqual(0, invalid.ExitCode);
        Assert.Contains("failed visudo syntax validation", invalid.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxValidator_ShouldRejectCrossFileSudoersAliasConflicts()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string duplicateHostAlias = "Host_Alias POWERFORGE_TARGETS = localhost\n";
        var result = RunValidator(
            sudoers: duplicateHostAlias + BuildExpectedSudoers(CaptureUser, "root"),
            additionalSudoers: duplicateHostAlias,
            visudoPathOverride: "/usr/sbin/visudo");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Combined managed sudoers policy failed visudo syntax validation", result.AllOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nopasswd:")]
    [InlineData("NoPasswd:")]
    public void Validator_ShouldRejectNonCanonicalNoPasswordTags(string tag)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root")
            .Replace("NOPASSWD:", tag, StringComparison.Ordinal);

        var result = RunValidator(sudoers: sudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("canonical case-sensitive NOPASSWD: tag", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldIgnoreNoPasswordTextInComments()
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root") +
                      "# lowercase nopasswd: text is only documentation\n";

        var result = RunValidator(sudoers: sudoers);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("3", result.StandardOutput.Trim());
    }

    [Fact]
    public void Validator_ShouldRejectMixedCanonicalAndNonCanonicalNoPasswordTags()
    {
        var extraSudoers = "deployment-user ALL=(root) NOPASSWD: /usr/bin/true, nopasswd: /usr/bin/false\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("canonical case-sensitive NOPASSWD: tag", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectUppercaseAliasPrincipals()
    {
        var extraSudoers = "Cmnd_Alias EXTRA = /bin/sh\n" +
                           "PF_BACKUP ALL=(root) NOPASSWD: EXTRA\n";

        var result = RunValidator(additionalSudoers: extraSudoers);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unsupported broad or aliased principal", result.AllOutput, StringComparison.Ordinal);
    }

}
