namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PinnedFixedProfileResolvesOnlyPreviouslyParsedCanonicalReferences(bool braced, bool unquoted)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;
        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "${HOME}/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var profile = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "env");
            var contents = File.ReadAllText(profile);
            foreach (var name in new[] { "ISSUER_ID", "KEY_ID", "PRIVATE_KEY_PATH" })
            {
                var variable = "APP_STORE_CONNECT_" + name;
                var reference = braced ? "${" + variable + "}" : "$" + variable;
                contents = contents.Replace("\"$" + variable + "\"", unquoted ? reference : "\"" + reference + "\"", StringComparison.Ordinal);
            }
            File.WriteAllText(profile, contents);
            var reader = RunPinnedFixedProfileReader(root, sandbox);
            reader.EnsureSuccess();
            Assert.Contains("FIXED_PROFILE_REFERENCE_PASS", reader.StandardOutput, StringComparison.Ordinal);

            // The actual entrypoint must pass profile intake and reach its source-pin guard.
            // The deliberately wrong owner pin prevents any CLI or Apple operation.
            var wrapper = RunPinnedFixedProfileWrapper(root, sandbox);
            Assert.NotEqual(0, wrapper.ExitCode);
            Assert.Contains("does not match reviewed commit", wrapper.StandardOutput + wrapper.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(TestIssuerId, wrapper.StandardOutput + wrapper.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeyId, wrapper.StandardOutput + wrapper.StandardError, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(sandbox); }
    }

    [Theory]
    [InlineData("${APP_STORE_CONNECT_KEY_ID")]
    [InlineData("$APP_STORE_CONNECT_KEY_ID}")]
    [InlineData("${APP_STORE_CONNECT_KEY_ID}}")]
    [InlineData("$ASC_KEY_ID")]
    [InlineData("$APP_STORE_CONNECT_FUTURE")]
    [InlineData("$app_store_connect_key_id")]
    [InlineData("$APP_STORE_CONNECT_KEY_ID/suffix")]
    [InlineData("prefix$APP_STORE_CONNECT_KEY_ID")]
    [InlineData("$(touch executed-marker)")]
    [InlineData("`touch executed-marker`")]
    public void PinnedFixedProfileRejectsMalformedUnknownAndExecutableReferences(string reference)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;
        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var profile = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "env");
            File.WriteAllText(profile, File.ReadAllText(profile).Replace("$APP_STORE_CONNECT_KEY_ID\"", reference + "\"", StringComparison.Ordinal));
            var result = RunPinnedFixedProfileReader(root, sandbox);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("must not execute shell expressions", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(sandbox, "executed-marker")));
            var wrapper = RunPinnedFixedProfileWrapper(root, sandbox);
            Assert.NotEqual(0, wrapper.ExitCode);
            Assert.Contains("must not execute shell expressions", wrapper.StandardOutput + wrapper.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(sandbox, "executed-marker")));
        }
        finally { DeletePinnedEvidenceDirectory(sandbox); }
    }

    [Theory]
    [InlineData("forward")]
    [InlineData("ambient")]
    [InlineData("unknown-home")]
    [InlineData("malformed-home")]
    public void PinnedFixedProfileDoesNotResolveFutureOrInheritedVariables(string shape)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;
        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var profile = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "env");
            var contents = File.ReadAllText(profile);
            contents = shape switch
            {
                "forward" => "export ASC_KEY_ID=\"${APP_STORE_CONNECT_KEY_ID}\"\n" + contents,
                "ambient" => contents.Replace($"export APP_STORE_CONNECT_KEY_ID=\"{TestKeyId}\"{Environment.NewLine}", "", StringComparison.Ordinal),
                "unknown-home" => contents.Replace("$HOME/", "$HOME_FUTURE/", StringComparison.Ordinal),
                _ => contents.Replace("$HOME/", "$HOME}/", StringComparison.Ordinal)
            };
            File.WriteAllText(profile, contents);
            var result = RunPinnedFixedProfileReader(root, sandbox, inheritedKeyId: TestKeyId);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(shape is "forward" or "ambient" ? "undefined credential variable" : "must not execute shell expressions", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(sandbox); }
    }

    [Theory]
    [InlineData("shared-profile", "permissions must not grant")]
    [InlineData("linked-profile", "symbolic link")]
    [InlineData("linked-key", "symbolic link")]
    [InlineData("hardlinked-key", "link")]
    public void PinnedFixedProfileReferenceResolutionRetainsPrivatePathGuards(string shape, string error)
    {
        if (!OperatingSystem.IsMacOS() || !CommandExists("pwsh")) return;
        var root = FindRepoRoot();
        var sandbox = CreateRunnerLocalSandbox(root, "$HOME/.appstoreconnect/AuthKey_ABC123DEFG.p8");
        try
        {
            var profile = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "env");
            var key = Path.Combine(sandbox, "runner-home", ".appstoreconnect", "AuthKey_ABC123DEFG.p8");
            if (shape == "shared-profile") SetUnixMode(profile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            else
            {
                var target = shape == "linked-profile" ? profile : key;
                var copy = Path.Combine(sandbox, "synthetic-copy");
                File.Move(target, copy);
                if (shape == "hardlinked-key") Run("ln", sandbox, copy, target).EnsureSuccess();
                else File.CreateSymbolicLink(target, copy);
            }
            var result = RunPinnedFixedProfileReader(root, sandbox);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(error, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(sandbox); }
    }

    private static ProcessResult RunPinnedFixedProfileReader(string root, string sandbox, string? inheritedKeyId = null)
    {
        var harness = Path.Combine(sandbox, "read-profile.ps1");
        File.WriteAllText(harness, FixedProfileReaderHarness);
        return RunWithEnvironment("pwsh", sandbox, PinnedProfileEnvironment(sandbox, inheritedKeyId),
            "-NoLogo", "-NoProfile", "-File", harness, "-Helper", Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.ps1"));
    }

    private static ProcessResult RunPinnedFixedProfileWrapper(string root, string sandbox) =>
        RunWithEnvironment("pwsh", sandbox, PinnedProfileEnvironment(sandbox), "-NoLogo", "-NoProfile", "-File",
            Path.Combine(root, "scripts", "Invoke-PinnedPowerForge.ps1"), "-ExpectedCommit", new string('0', 40),
            "-ConsumerRoot", sandbox, "-ExpectedConsumerRepository", "Synthetic/FixedProfile", "-LoadFixedLocalAppleProfile", "apple-testflight", "worker");

    private static Dictionary<string, string?> PinnedProfileEnvironment(string sandbox, string? inheritedKeyId = null)
    {
        var environment = new Dictionary<string, string?> { ["HOME"] = Path.Combine(sandbox, "runner-home") };
        foreach (var name in new[] { "APP_STORE_CONNECT_ISSUER_ID", "APP_STORE_CONNECT_KEY_ID", "APP_STORE_CONNECT_PRIVATE_KEY_PATH", "APP_STORE_CONNECT_PRIVATE_KEY", "ASC_ISSUER_ID", "ASC_KEY_ID", "ASC_PRIVATE_KEY_PATH", "ASC_PRIVATE_KEY" }) environment[name] = string.Empty;
        if (inheritedKeyId is not null) environment["APP_STORE_CONNECT_KEY_ID"] = inheritedKeyId;
        return environment;
    }

    private const string FixedProfileReaderHarness = """
        param([string]$Helper)
        $ErrorActionPreference='Stop'
        $tokens=$null; $errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile($Helper,[ref]$tokens,[ref]$errors)
        if($errors.Count){throw 'Pinned helper failed to parse.'}
        foreach($name in @('Assert-UnlinkedPath','Assert-PrivateUnixPath','Get-SavedEnvironmentValue','Assert-FixedLocalCredentialProfile','Read-FixedLocalAppleProfile')) {
            $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
            if($null -eq $function){throw 'Required production profile function was not found.'}
            Invoke-Expression $function.Extent.Text
        }
        $values=Read-FixedLocalAppleProfile
        Assert-FixedLocalCredentialProfile -Saved $values
        foreach($suffix in @('ISSUER_ID','KEY_ID','PRIVATE_KEY_PATH')) {
            if($values['ASC_'+$suffix] -ne $values['APP_STORE_CONNECT_'+$suffix]){throw 'Resolved alias differs from the canonical parsed value.'}
        }
        if($script:validatedCredentialEnvironment.Count -ne 3){throw 'The complete canonical credential tuple was not validated.'}
        'FIXED_PROFILE_REFERENCE_PASS'
        """;
}
