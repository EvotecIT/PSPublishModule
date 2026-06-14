using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static readonly JsonSerializerOptions ActionContextJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private void ExecuteActions(
        ModulePipelineActionStage stage,
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        var actions = (plan.Actions ?? Array.Empty<ConfigurationActionSegment>())
            .Where(action => action is not null && action.Configuration?.Enabled == true && action.Configuration.At == stage)
            .ToArray();

        foreach (var action in actions)
        {
            var step = session.GetActionStep(action);
            session.Start(step);
            try
            {
                var context = BuildActionContext(stage, action, plan, state);
                var contextPath = CreateActionContextPath(context, plan, action);
                context.ContextPath = contextPath;
                File.WriteAllText(contextPath, JsonSerializer.Serialize(context, ActionContextJsonOptions));

                var result = _hostedOperations.RunAction(action.Configuration, context, contextPath, plan.ProjectRoot);
                result.ContinuedOnError = !result.Succeeded && action.Configuration.ContinueOnError;
                state.ActionResults.Add(result);

                if (!result.Succeeded)
                {
                    var message = BuildActionFailureMessage(result);
                    if (action.Configuration.ContinueOnError)
                        _logger.Warn(message);
                    else
                        throw new InvalidOperationException(message);
                }

                session.Done(step);
            }
            catch (Exception ex)
            {
                session.Fail(step, ex);
                throw;
            }
        }
    }

    private ModulePipelineActionContext BuildActionContext(
        ModulePipelineActionStage stage,
        ConfigurationActionSegment action,
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var buildResult = state.BuildResult;
        var actionName = string.IsNullOrWhiteSpace(action.Configuration?.Name)
            ? stage.ToString()
            : action.Configuration!.Name!.Trim();

        return new ModulePipelineActionContext
        {
            Stage = stage,
            ActionName = actionName,
            ModuleName = plan.ModuleName,
            ProjectRoot = plan.ProjectRoot,
            ExpectedVersion = plan.ExpectedVersion,
            ResolvedVersion = plan.ResolvedVersion,
            PreRelease = plan.PreRelease,
            StagingPath = buildResult?.StagingPath ?? state.StagingPathForCleanup ?? plan.BuildSpec.StagingPath,
            ManifestPath = buildResult?.ManifestPath,
            ModuleRoot = buildResult?.StagingPath ?? state.StagingPathForCleanup,
            DocumentationPath = plan.Documentation is null ? null : ResolvePath(plan.ProjectRoot, plan.Documentation.Path),
            DocumentationReadmePath = plan.Documentation is null ? null : ResolvePath(plan.ProjectRoot, plan.Documentation.PathReadme, optional: true),
            ArtefactPaths = BuildArtefactPaths(state.ArtefactResults),
            PublishDestinations = BuildPublishDestinations(state.PublishResults)
        };
    }

    private static string CreateActionContextPath(
        ModulePipelineActionContext context,
        ModulePipelinePlan plan,
        ConfigurationActionSegment action)
    {
        var actionName = string.IsNullOrWhiteSpace(context.ActionName) ? action.Configuration.At.ToString() : context.ActionName;
        var safeName = SanitizeFileName(actionName);
        var root = Path.Combine(Path.GetTempPath(), "PowerForge", "actions", plan.ModuleName, $"{action.Configuration.At}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"{safeName}.context.json");
    }

    private static string[] BuildArtefactPaths(IEnumerable<ArtefactBuildResult> artefacts)
        => (artefacts ?? Array.Empty<ArtefactBuildResult>())
            .Select(SelectArtefactPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? SelectArtefactPath(ArtefactBuildResult result)
    {
        if (result is null) return null;
        if (!string.IsNullOrWhiteSpace(result.OutputPath)) return result.OutputPath;
        return null;
    }

    private static string[] BuildPublishDestinations(IEnumerable<ModulePublishResult> publishes)
        => (publishes ?? Array.Empty<ModulePublishResult>())
            .Where(static result => result is not null)
            .Select(static result => string.IsNullOrWhiteSpace(result.RepositoryName)
                ? result.Destination.ToString()
                : $"{result.Destination}:{result.RepositoryName}")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildActionFailureMessage(ModulePipelineActionResult result)
    {
        var name = string.IsNullOrWhiteSpace(result.Name) ? result.Stage.ToString() : result.Name;
        var detail = string.Join(
            Environment.NewLine,
            new[] { result.StdErr, result.StdOut }
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Select(static text => text.Trim()));
        return string.IsNullOrWhiteSpace(detail)
            ? $"Lifecycle action '{name}' failed at {result.Stage} (exit {result.ExitCode})."
            : $"Lifecycle action '{name}' failed at {result.Stage} (exit {result.ExitCode}).{Environment.NewLine}{detail}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? string.Empty)
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "action" : safe;
    }
}
