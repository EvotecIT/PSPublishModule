using PowerForge;
using System.Runtime.InteropServices;

namespace PowerForge.Tests;

public sealed class ModuleBuildPathPolicyTests
{
    [Fact]
    public void ResolveTokenAwareConfigPathNullable_preserves_tokenized_relative_paths()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "pf-path-policy-" + Guid.NewGuid().ToString("N"));

        var resolved = ModuleBuildPathPolicy.ResolveTokenAwareConfigPathNullable(projectRoot, @"Artefacts\Packed\<TagModuleVersionWithPreRelease>");

        Assert.Equal("Artefacts/Packed/<TagModuleVersionWithPreRelease>", resolved);
    }

    [Fact]
    public void ResolveRootedConfigPaths_only_resolves_rooted_paths()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "pf-path-policy-" + Guid.NewGuid().ToString("N"));
        var rootedPath = Path.Combine(projectRoot, "Help", "Rooted");

        var resolved = ModuleBuildPathPolicy.ResolveRootedConfigPaths(projectRoot, new[] { "Help/About", rootedPath });

        Assert.Equal(new[] { "Help/About", Path.GetFullPath(rootedPath) }, resolved);
    }

    [Fact]
    public void MakeRelativeForProjectRoot_preserves_external_rooted_paths_when_requested()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "pf-path-policy-workspace-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(workspaceRoot, "Module");
        var externalRoot = Path.Combine(Path.GetTempPath(), "pf-path-policy-external-" + Guid.NewGuid().ToString("N"));
        var externalPath = Path.Combine(externalRoot, "Packages");
        var workspaceSiblingPath = Path.Combine(workspaceRoot, "Artefacts", "Packages");

        var preservedExternal = ModuleBuildPathPolicy.MakeRelativeForProjectRoot(projectRoot, externalPath, preserveExternalRooted: true, workspaceRoot);
        var workspaceRelative = ModuleBuildPathPolicy.MakeRelativeForProjectRoot(projectRoot, workspaceSiblingPath, preserveExternalRooted: true, workspaceRoot);

        Assert.Equal(NormalizeForJson(Path.GetFullPath(externalPath)), preservedExternal);
        Assert.Equal("../Artefacts/Packages", workspaceRelative);
    }

    [Fact]
    public void MakeRelativeForProjectRoot_preserves_token_intent_without_resolving_tokens()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "pf-path-policy-workspace-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(workspaceRoot, "Module");
        var projectPath = Path.Combine(projectRoot, "Artefacts", "Packed", "<TagModuleVersionWithPreRelease>");
        var externalPath = Path.Combine(Path.GetTempPath(), "pf-path-policy-external-" + Guid.NewGuid().ToString("N"), "Packed", "<ModuleVersion>");

        var projectRelative = ModuleBuildPathPolicy.MakeRelativeForProjectRoot(projectRoot, projectPath, preserveExternalRooted: true, workspaceRoot);
        var externalPreserved = ModuleBuildPathPolicy.MakeRelativeForProjectRoot(projectRoot, externalPath, preserveExternalRooted: true, workspaceRoot);

        Assert.Equal("Artefacts/Packed/<TagModuleVersionWithPreRelease>", projectRelative);
        Assert.Equal(NormalizeForJson(externalPath), externalPreserved);
    }

    [Fact]
    public void IsSameOrChildPath_uses_platform_path_case_semantics()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        var matched = ModuleBuildPathPolicy.IsSameOrChildPath("/tmp/Repo", "/tmp/repo/secrets");

        Assert.Equal(expected, matched);
    }

    private static string NormalizeForJson(string path)
        => path.Replace('\\', '/');
}
