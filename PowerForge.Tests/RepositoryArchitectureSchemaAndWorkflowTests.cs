using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class RepositoryArchitectureSchemaAndWorkflowTests
{
    [Fact]
    public void Schema_AcceptsOwnedCapabilityAndRejectsUnknownPolicyKnobs()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(GetRepoPath(
            "Schemas",
            "powerforge.repository-architecture.schema.json")));
        var valid = JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "repositoryRoot": "..",
              "projectRules": [
                {
                  "id": "core",
                  "project": "Core/Core.csproj",
                  "allowedProjectReferences": []
                }
              ],
              "capabilities": [
                {
                  "id": "projection",
                  "ownerProjects": ["Core/Core.csproj"],
                  "ownerPaths": ["Core/Projection*.cs"],
                  "consumerProjects": ["Excel/Excel.csproj"],
                  "requiredEvidenceKinds": ["contract"],
                  "evidence": [
                    {
                      "id": "contracts",
                      "kind": "contract",
                      "stepId": "projection-contracts",
                      "coversProjects": ["Core/Core.csproj", "Excel/Excel.csproj"]
                    }
                  ]
                }
              ]
            }
            """)!;
        var invalid = valid.DeepClone();
        invalid["inventedArchitectureBrain"] = true;
        var missingRequiredCapabilityMembers = JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "capabilities": [
                {
                  "id": "projection"
                }
              ]
            }
            """)!;
        var emptyCapability = JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "capabilities": [
                {
                  "id": "projection",
                  "ownerProjects": [],
                  "ownerPaths": [],
                  "consumerProjects": [],
                  "evidence": []
                }
              ]
            }
            """)!;

        Assert.True(schema.Evaluate(valid, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        Assert.False(schema.Evaluate(invalid, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        Assert.False(schema.Evaluate(missingRequiredCapabilityMembers, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        Assert.False(schema.Evaluate(emptyCapability, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Fact]
    public void ReusableWorkflow_DelegatesPolicyToOneSharedActionAndPinnedSource()
    {
        var workflow = File.ReadAllText(GetRepoPath(
            ".github",
            "workflows",
            "powerforge-repository-architecture.yml"));
        var action = File.ReadAllText(GetRepoPath(
            ".github",
            "actions",
            "repository-architecture",
            "action.yml"));

        Assert.Contains("powerforge_ref:", workflow, StringComparison.Ordinal);
        Assert.Contains("required: true", workflow, StringComparison.Ordinal);
        Assert.Contains("^[0-9a-fA-F]{40}$", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: ./.powerforge/runtime/.github/actions/repository-architecture", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("run_evidence", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", workflow, StringComparison.Ordinal);
        Assert.Contains("'architecture', 'verify'", action, StringComparison.Ordinal);
        Assert.Contains("'--run-evidence'", action, StringComparison.Ordinal);
        Assert.DoesNotContain("ARCHITECTURE_RUN_EVIDENCE", action, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedProjectReferences", action, StringComparison.Ordinal);
        Assert.DoesNotContain("usagePatterns", action, StringComparison.Ordinal);
    }

    private static string GetRepoPath(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return Path.Combine([current.FullName, .. relativePath]);
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }
}
