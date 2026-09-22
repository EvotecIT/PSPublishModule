using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class WorkspacePathContainmentTests
{
    [Fact]
    public void FilesystemRootContainsDescendantsAndNamedWorkspaceExcludesSiblingPrefixes()
    {
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var workspace = Path.Combine(filesystemRoot, "StudioWorkspace");
        var child = Path.Combine(workspace, "Project");

        Assert.True(WorkspacePathContainment.ContainsOrEquals(filesystemRoot, child));
        Assert.True(WorkspacePathContainment.ContainsOrEquals(workspace + Path.DirectorySeparatorChar, child));
        Assert.False(WorkspacePathContainment.ContainsOrEquals(workspace, workspace + "-other"));
    }
}
