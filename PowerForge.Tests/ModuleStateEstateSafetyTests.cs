using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleStateEstateSafetyTests
{
    [Fact]
    public void PathIdentity_PreservesFileSystemRootAndMatchesChildren()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("."));

        Assert.False(string.IsNullOrWhiteSpace(root));
        var normalizedRoot = ModuleStatePathIdentity.Normalize(root!);
        Assert.EndsWith("/", normalizedRoot, StringComparison.Ordinal);
        Assert.True(ModuleStatePathIdentity.Equals(root, normalizedRoot));
        Assert.True(ModuleStatePathIdentity.IsSameOrChild(Path.Combine(root!, "estate", "Modules"), root));
    }

    [Fact]
    public void InventoryPathDiagnostic_UsesRequirednessForSeverity()
    {
        using var workspace = new TemporaryDirectory();
        var optional = ModuleStateInventoryService.CreatePathDiagnostic(
            new ModuleStateModulePath(Path.Combine(workspace.Path, "optional"), isRequired: false),
            "ModuleState.InventoryPathEnumerationFailed",
            "optional failed");
        var required = ModuleStateInventoryService.CreatePathDiagnostic(
            new ModuleStateModulePath(Path.Combine(workspace.Path, "required"), isRequired: true),
            "ModuleState.InventoryPathEnumerationFailed",
            "required failed");

        Assert.Equal(ModuleStateConflictSeverity.Warning, optional.Severity);
        Assert.Equal(ModuleStateConflictSeverity.Error, required.Severity);
    }
}
