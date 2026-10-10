using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static void ComposeProjectVersionUpdates(
        string root,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        List<KeyValuePair<DotNetRepositoryProjectResult, RepositoryTextFileUpdate>> pendingUpdates,
        ProjectVersionBindingFileUpdate[] bindings,
        IReadOnlyList<ProjectVersionBinding>? configuredBindings,
        StringComparer pathComparer)
    {
        BoundProjectVersionService.ValidatePropertyBindings(root, projects, bindings);
        foreach (var project in projects)
        {
            var projectPath = Path.GetFullPath(project.CsprojPath);
            var pendingIndex = pendingUpdates.FindIndex(item => pathComparer.Equals(Path.GetFullPath(item.Value.FilePath), projectPath));
            var bindingIndex = Array.FindIndex(bindings, item => pathComparer.Equals(Path.GetFullPath(item.Update.FilePath), projectPath));
            if (pendingIndex < 0 && (bindingIndex < 0 || !bindings[bindingIndex].HasChanges))
                continue;

            // Bindings can edit a project whose literal version is already current.
            // Validate those edits as well as the editor's pending version changes.
            var original = pendingIndex < 0 ? File.ReadAllText(projectPath) : pendingUpdates[pendingIndex].Value.OriginalContent;
            var planned = pendingIndex < 0 ? original : pendingUpdates[pendingIndex].Value.UpdatedContent;
            var composed = bindingIndex >= 0 ? bindings[bindingIndex].Update.UpdatedContent : pendingUpdates[pendingIndex].Value.UpdatedContent;
            var preserved = BoundProjectVersionService.PreserveReferences(
                root, projectPath, original, composed, planned, project.NewVersion!, bindings, configuredBindings);
            if (bindingIndex >= 0)
            {
                var binding = bindings[bindingIndex];
                bindings[bindingIndex] = new ProjectVersionBindingFileUpdate(
                    new RepositoryTextFileUpdate(binding.Update.FilePath, binding.Update.OriginalContent, preserved),
                    binding.RelativePath, binding.BindingCount);
            }
            project.HasPendingVersionUpdate = !string.Equals(original, preserved, StringComparison.Ordinal);
            if (!project.HasPendingVersionUpdate)
            {
                if (pendingIndex >= 0)
                    pendingUpdates.RemoveAt(pendingIndex);
            }
            else
            {
                var pending = new KeyValuePair<DotNetRepositoryProjectResult, RepositoryTextFileUpdate>(
                    project, new RepositoryTextFileUpdate(projectPath, original, preserved));
                if (pendingIndex < 0)
                    pendingUpdates.Add(pending);
                else
                    pendingUpdates[pendingIndex] = pending;
            }
        }
    }
}
