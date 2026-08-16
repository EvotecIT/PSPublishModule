using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("- IntelligenceX.OpenAI.Auth.AuthBundle — Represents an authentication bundle for OpenAI providers.")]
    [InlineData("- IntelligenceX.OpenAI.Auth.FileAuthBundleStore — File-based authentication bundle store with optional encryption.")]
    [InlineData("- public const CopilotCliInstallMethod Npm — Install via npm.")]
    [InlineData("Represents an authentication bundle for OpenAI providers.")]
    [InlineData("- public Boolean IsExpired(Nullable<DateTimeOffset> now = null) — Returns whether the bundle is expired.")]
    [InlineData("- public static String Serialize(AuthBundle bundle) — Serializes an auth bundle to JSON text.")]
    [InlineData("- public static AuthBundle Deserialize(String json) — Deserializes an auth bundle from JSON text.")]
    [InlineData("- public static String ResolveAuthPath() — Resolves the default auth bundle path.")]
    public void Scan_IgnoresPackageManagerWordsInGeneratedApiProse(string prose)
    {
        using var scanner = new WebAgentContentSecurityScanner();
        var root = CreateArtifact("llms-full.txt", prose);

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms-full.txt"],
                VerifyPackages = false
            });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(0, result.PackageReferenceCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("npm ci-test")]
    [InlineData("- npm ci-test")]
    [InlineData("> npm ci-test")]
    [InlineData("Run npm ci-test")]
    [InlineData("Use `npm ci-test` for validation.")]
    [InlineData("```shell\nnpm ci-test\n```")]
    public void Scan_StillRejectsCommandShapedUnsupportedPackageOperations(string command)
    {
        using var scanner = new WebAgentContentSecurityScanner();
        var root = CreateArtifact("llms.txt", command);

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.txt"],
                VerifyPackages = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, static finding =>
                finding.Code == "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
