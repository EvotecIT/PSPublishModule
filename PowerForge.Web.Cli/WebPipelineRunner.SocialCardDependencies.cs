using PowerForge.Web;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    /// <summary>Inspects active build/API steps using the same inherited configuration as execution.</summary>
    internal static bool RequiresSocialCardDependencies(string pipelinePath, string? mode)
    {
        using var document = LoadPipelineDocumentWithExtends(pipelinePath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(pipelinePath))!;
        var effectiveMode = string.IsNullOrWhiteSpace(mode) ? "ci" : mode.Trim();
        foreach (var definition in BuildStepDefinitions(document.RootElement.GetProperty("steps")))
        {
            var step = definition.Element;
            if (GetSkipReason(definition.Task, step, effectiveMode, null, null) is not null)
                continue;
            var api = definition.Task is "apidocs" or "project-apidocs" or "project-apidoc" or "project-api-docs";
            var doctor = definition.Task == "doctor";
            if (!api && definition.Task != "build" && !doctor)
                continue;
            if (doctor && !(GetBool(step, "build") ?? !(GetBool(step, "noBuild") ?? false)))
                continue;
            if (StepRequiresSocialCards(step, baseDir, api, doctor))
                return true;
        }
        return false;
    }

    private static bool StepRequiresSocialCards(JsonElement step, string baseDir, bool api, bool doctor = false)
    {
        var inputs = api ? GetArrayOfObjects(step, "inputs") ?? GetArrayOfObjects(step, "entries") : null;
        if (inputs is { Length: > 0 })
        {
            foreach (var input in inputs)
            {
                using var merged = CreateMergedApiDocsStepDocument(step, input);
                if (StepRequiresSocialCards(merged.RootElement, baseDir, api: true))
                    return true;
            }
            return false;
        }

        // Doctor builds its loaded site directly and does not apply build-step overrides.
        var generate = doctor ? null : GetBool(step, "socialAutoGenerate") ?? GetBool(step, "social-auto-generate");
        if (generate is not null)
            return generate.Value;

        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(configPath) && api)
            configPath = Path.Combine(baseDir, "site.json");
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return false;
        var (spec, _) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
        return spec.Social?.AutoGenerateCards == true;
    }
}
