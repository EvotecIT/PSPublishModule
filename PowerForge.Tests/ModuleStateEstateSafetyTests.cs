using PowerForge;
using PSPublishModule;

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
    public void PathIdentity_UsesPlatformNativeBackslashSemantics()
    {
        var root = Path.Combine(Path.GetTempPath(), "estate");
        var literalBackslashPath = Path.Combine(root, "Modules\\Prod");
        var directoryPath = Path.Combine(root, "Modules", "Prod");

        Assert.Equal(
            FrameworkCompatibility.IsWindows(),
            ModuleStatePathIdentity.Equals(literalBackslashPath, directoryPath));
        if (FrameworkCompatibility.IsWindows())
            Assert.DoesNotContain("\\", ModuleStatePathIdentity.Normalize(literalBackslashPath));
        else
            Assert.Contains("\\", ModuleStatePathIdentity.Normalize(literalBackslashPath));
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

    [Fact]
    public void Cleanup_DoesNotMutateWhenRefreshedPlanHasErrors()
    {
        using var workspace = new TemporaryDirectory();
        var moduleRoot = Path.Combine(workspace.Path, "Modules");
        var targetPath = Path.Combine(moduleRoot, "Company.Tools", "1.0.0");
        Directory.CreateDirectory(targetPath);
        var plan = new ModuleStatePlan(
            new[]
            {
                new ModuleStatePlanAction(
                    ModuleStatePlanActionKind.Remove,
                    "Company.Tools",
                    "1.0.0",
                    "1.0.0",
                    "cleanup",
                    targetPath: targetPath,
                    targetModuleRoot: moduleRoot)
            },
            new[]
            {
                new ModuleStateConflictFinding(
                    ModuleStateConflictSeverity.Error,
                    "ModuleState.InventoryPathEnumerationFailed",
                    "A required estate root became inaccessible.",
                    string.Empty,
                    new[] { "Company.Tools" },
                    new[] { "1.0.0" },
                    path: moduleRoot)
            });

        var result = new ModuleStateManagedCleanupService(new RepairManagedModuleCommand()).Execute(
            plan,
            new ModuleStateInventoryResult(),
            new ModuleStateManagedDeliveryOptions());

        var failure = Assert.Single(result);
        Assert.False(failure.Succeeded);
        Assert.Equal("Cleanup", failure.Operation);
        Assert.Contains("contains errors", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(targetPath));
    }
}
