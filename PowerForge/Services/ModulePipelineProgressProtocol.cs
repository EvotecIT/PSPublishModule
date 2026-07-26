using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Internal line protocol used to carry module pipeline progress from the isolated
/// PowerShell build host back to the unified release renderer.
/// </summary>
internal static class ModulePipelineProgressProtocol
{
    internal const string EnvironmentVariable = "POWERFORGE_MODULE_PROGRESS_PROTOCOL";
    private const string Prefix = "##powerforge-module-progress-v1##";

    internal static IModulePipelineProgressReporter? CreateReporterFromEnvironment(ModulePipelinePlan plan)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnvironmentVariable),
                "1",
                StringComparison.Ordinal))
            return null;

        return new ProtocolReporter(plan);
    }

    internal static bool TryParse(
        string? line,
        out ModulePipelineProgressProtocolMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line) ||
            !line!.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            var payload = line.Substring(Prefix.Length);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            message = JsonSerializer.Deserialize<ModulePipelineProgressProtocolMessage>(json);
            return message is not null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsProtocolLine(string? line)
        => !string.IsNullOrWhiteSpace(line) &&
           line!.StartsWith(Prefix, StringComparison.Ordinal);

    private static void Write(ModulePipelineProgressProtocolMessage message)
    {
        var json = JsonSerializer.Serialize(message);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        Console.WriteLine(Prefix + payload);
    }

    private sealed class ProtocolReporter : IModulePipelineProgressReporterV3
    {
        private readonly IReadOnlyDictionary<string, PowerForgeReleaseProgressItem> _items;

        internal ProtocolReporter(ModulePipelinePlan plan)
        {
            var steps = ModulePipelineStep.Create(plan);
            var items = steps
                .Select((step, index) => new PowerForgeReleaseProgressItem
                {
                    Phase = PowerForgeReleaseProgressPhase.Module,
                    Key = step.Key,
                    Title = step.Title,
                    Kind = step.Kind.ToString(),
                    Position = index + 1,
                    Total = steps.Length
                })
                .ToArray();
            _items = items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
            Write(new ModulePipelineProgressProtocolMessage { Items = items });
        }

        public void StepStarting(ModulePipelineStep step)
            => Update(step, PowerForgeReleaseProgressItemState.Started);

        public void StepCompleted(ModulePipelineStep step)
            => Update(step, PowerForgeReleaseProgressItemState.Completed);

        public void StepFailed(ModulePipelineStep step, Exception error)
            => Update(step, PowerForgeReleaseProgressItemState.Failed, error.Message);

        public void StepSkipped(ModulePipelineStep step)
            => Update(step, PowerForgeReleaseProgressItemState.Skipped);

        public void StepProgress(ModulePipelineStep step, double value, double maximum, string? detail = null)
        {
            if (step is null || !_items.TryGetValue(step.Key, out var item))
                return;

            item.ProgressValue = Math.Max(0, value);
            item.ProgressMaximum = Math.Max(0, maximum);
            Update(step, PowerForgeReleaseProgressItemState.Started, detail);
        }

        private void Update(
            ModulePipelineStep step,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
        {
            if (step is null || !_items.TryGetValue(step.Key, out var item))
                return;

            Write(new ModulePipelineProgressProtocolMessage
            {
                Item = item,
                State = state,
                Detail = detail
            });
        }
    }
}

internal sealed class ModulePipelineProgressProtocolMessage
{
    public PowerForgeReleaseProgressItem[]? Items { get; set; }

    public PowerForgeReleaseProgressItem? Item { get; set; }

    public PowerForgeReleaseProgressItemState? State { get; set; }

    public string? Detail { get; set; }
}
