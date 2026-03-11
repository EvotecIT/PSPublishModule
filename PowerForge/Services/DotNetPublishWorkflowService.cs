using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class DotNetPublishWorkflowService
{
    private readonly ILogger _logger;
    private readonly Func<DotNetPublishSpec, string?, DotNetPublishPlan> _planPublish;
    private readonly Func<DotNetPublishPlan, IDotNetPublishProgressReporter?, DotNetPublishResult> _runPublish;
    private readonly Action<DotNetPublishSpec, string> _writeSpecJson;

    public DotNetPublishWorkflowService(
        ILogger logger,
        Func<DotNetPublishSpec, string?, DotNetPublishPlan>? planPublish = null,
        Func<DotNetPublishPlan, IDotNetPublishProgressReporter?, DotNetPublishResult>? runPublish = null,
        Action<DotNetPublishSpec, string>? writeSpecJson = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _planPublish = planPublish ?? ((spec, sourceLabel) => new DotNetPublishPipelineRunner(_logger).Plan(spec, sourceLabel));
        _runPublish = runPublish ?? ((plan, progress) => new DotNetPublishPipelineRunner(_logger).Run(plan, progress));
        _writeSpecJson = writeSpecJson ?? WriteSpecJson;
    }

    public DotNetPublishWorkflowResult Execute(DotNetPublishPreparedContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (context.Spec is null)
            throw new ArgumentException("Prepared spec is required.", nameof(context));

        if (context.JsonOnly)
        {
            if (string.IsNullOrWhiteSpace(context.JsonOutputPath))
                throw new InvalidOperationException("JSON output path was not prepared.");

            var jsonOutputPath = context.JsonOutputPath!;
            _writeSpecJson(context.Spec, jsonOutputPath);
            _logger.Success($"Wrote DotNet publish JSON: {jsonOutputPath}");
            return new DotNetPublishWorkflowResult
            {
                JsonOutputPath = jsonOutputPath
            };
        }

        var plan = _planPublish(context.Spec, context.SourceLabel);
        if (context.PlanOnly || context.ValidateOnly)
        {
            if (context.ValidateOnly)
                _logger.Success($"DotNet publish config is valid ({plan.Steps.Length} step(s), {plan.Targets.Length} target(s)).");

            return new DotNetPublishWorkflowResult
            {
                Plan = plan
            };
        }

        return new DotNetPublishWorkflowResult
        {
            Result = _runPublish(plan, null)
        };
    }

    private static void WriteSpecJson(DotNetPublishSpec spec, string jsonFullPath)
    {
        var outDir = Path.GetDirectoryName(jsonFullPath);
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(spec, options) + Environment.NewLine;
        File.WriteAllText(jsonFullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
