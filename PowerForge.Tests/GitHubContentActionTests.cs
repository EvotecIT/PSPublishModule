using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Tests;

public sealed class GitHubContentActionTests
{
    [Fact]
    public void ExampleConfig_DeserializesToOptInTieredOutputs()
    {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot, "Examples", "GitHubContent", "github-content.json");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<GitHubRepositoryContentSpec>(File.ReadAllText(configPath), options);

        Assert.NotNull(spec);
        Assert.True(spec.Sponsors.Enabled);
        Assert.True(spec.Sponsors.TierRecognition.Enabled);
        Assert.Equal(2, spec.Sponsors.Outputs.Length);
        Assert.Contains(spec.Sponsors.Outputs, output => output.Layout == GitHubSponsorsMarkdownLayout.Full && output.CreateIfMissing);
        Assert.Contains(spec.Sponsors.Outputs, output => output.Layout == GitHubSponsorsMarkdownLayout.Compact && !output.CreateIfMissing);
    }

    [Fact]
    public void Schema_AllowsEnvironmentBasedSponsorableLoginFallback()
    {
        var repoRoot = FindRepoRoot();
        var schemaPath = Path.Combine(repoRoot, "Schemas", "github.content.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var required = schema.RootElement
            .GetProperty("definitions")
            .GetProperty("sponsors")
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.DoesNotContain("sponsorableLogin", required);
        Assert.Contains("enabled", required);
        Assert.Contains("outputs", required);
    }

    [Fact]
    public void CompositeAction_ReportsOnlyChangedDocumentsInsideCallerWorkspace()
    {
        var repoRoot = FindRepoRoot();
        var action = File.ReadAllText(Path.Combine(repoRoot, ".github", "actions", "github-content", "action.yml"));

        Assert.Contains("github content sync", action, StringComparison.Ordinal);
        var client = File.ReadAllText(Path.Combine(repoRoot, "PowerForge", "Services", "GitHubSponsorsClient.cs"));
        Assert.Contains("includePrivate", client, StringComparison.Ordinal);
        Assert.Contains("repositoryOwner(login: $login)", client, StringComparison.Ordinal);
        Assert.Contains("Generated document is outside the caller workspace", action, StringComparison.Ordinal);
        Assert.Contains("--restrict-output-root $workspace", action, StringComparison.Ordinal);
        Assert.Contains("changed-paths-json", action, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN: ${{ inputs['github-token'] }}", action, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusableWorkflow_StagesReportedPathsInsteadOfTheRuntimeCheckout()
    {
        var repoRoot = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "powerforge-github-content.yml"));

        Assert.Contains("permissions:\n  contents: write", Normalize(workflow), StringComparison.Ordinal);
        Assert.Contains("if: inputs.commit_changes && steps.content.outputs.changed == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("git add -- $path", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git add --all", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git add .", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".powerforge-runtime/pspublishmodule", workflow, StringComparison.Ordinal);
        Assert.Contains("job.workflow_repository", workflow, StringComparison.Ordinal);
        Assert.Contains("job.workflow_sha", workflow, StringComparison.Ordinal);
        Assert.Contains("default: \"\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("default: \"main\"", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicDocumentation_LinksTheConfigAndReusableWorkflow()
    {
        var repoRoot = FindRepoRoot();
        var docs = File.ReadAllText(Path.Combine(repoRoot, "Docs", "PowerForge.GitHubContent.md"));
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));

        Assert.Contains("tierRecognition.enabled", docs, StringComparison.Ordinal);
        Assert.Contains("includePrivate: false", docs, StringComparison.Ordinal);
        Assert.Contains("powerforge-github-content.yml", docs, StringComparison.Ordinal);
        Assert.Contains("Docs/PowerForge.GitHubContent.md", readme, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 12 && current is not null; index++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the PowerForge repository root.");
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n');
}
