namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static void SetAggregateProjectError(
        DotNetRepositoryReleaseResult result,
        IReadOnlyList<DotNetRepositoryProjectResult> projects)
    {
        var projectErrors = projects
            .Where(project => !string.IsNullOrWhiteSpace(project.ErrorMessage))
            .Select(project => $"{project.ProjectName}: {project.ErrorMessage}")
            .ToArray();
        if (projectErrors.Length > 0 && string.IsNullOrWhiteSpace(result.ErrorMessage))
            result.ErrorMessage = "One or more projects failed: " + string.Join("; ", projectErrors);
    }
}
