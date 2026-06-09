using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private void EnsureOutputDirectoryUnlocked(
        DotNetPublishPlan plan,
        string outputDir,
        string contextLabel,
        string? serviceName)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (!plan.LockedOutputGuard) return;
        if (string.IsNullOrWhiteSpace(outputDir)) return;
        if (!Directory.Exists(outputDir)) return;

        var sampleLimit = plan.LockedOutputSampleLimit < 1 ? 1 : plan.LockedOutputSampleLimit;
        var locked = new List<string>(sampleLimit);

        foreach (var file in EnumerateFilesSafe(outputDir, "*", SearchOption.AllDirectories))
        {
            if (!IsFileExclusivelyAccessible(file))
            {
                locked.Add(file);
                if (locked.Count >= sampleLimit)
                    break;
            }
        }

        if (locked.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Output directory contains locked file(s) for {contextLabel}: {outputDir}");
        sb.AppendLine("Sample locked files:");
        foreach (var file in locked)
            sb.AppendLine($" - {file}");
        sb.AppendLine("Suggested actions:");
        if (!string.IsNullOrWhiteSpace(serviceName))
            sb.AppendLine($" - Stop service '{serviceName}' and retry.");
        sb.AppendLine(" - Close processes/editors/terminals using files from the output directory.");
        sb.AppendLine(" - Retry, or tune DotNet.OnLockedOutput / DotNet.LockedOutputGuard in config if intentional.");

        HandlePolicy(plan.OnLockedOutput, sb.ToString().TrimEnd());
    }

    private static bool IsFileExclusivelyAccessible(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return true;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
