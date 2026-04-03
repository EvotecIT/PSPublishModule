using System.IO;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal void SyncSourceProjectVersionIfRequested(ModulePipelinePlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (!plan.SyncNETProjectVersion)
            return;

        var csprojPath = plan.ResolvedCsprojPath?.Trim();
        if (string.IsNullOrWhiteSpace(csprojPath))
        {
            throw new InvalidOperationException(
                "SyncNETProjectVersion was enabled, but no .csproj path could be resolved. Configure Build.CsprojPath or NETProjectPath/NETProjectName.");
        }

        var fullPath = Path.GetFullPath(csprojPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"SyncNETProjectVersion was enabled, but the project file was not found: {fullPath}", fullPath);

        var content = File.ReadAllText(fullPath);
        var updated = CsprojVersionEditor.UpdateVersionText(content, plan.ResolvedVersion, out var hadVersionTag);
        if (string.Equals(content, updated, StringComparison.Ordinal))
        {
            _logger.Info($"Build: source project version already matches resolved module version '{plan.ResolvedVersion}' ({fullPath}).");
            return;
        }

        File.WriteAllText(fullPath, updated);
        var action = hadVersionTag ? "synchronized" : "inserted VersionPrefix for";
        _logger.Info($"Build: {action} source project version '{plan.ResolvedVersion}' in '{fullPath}'.");
    }
}
