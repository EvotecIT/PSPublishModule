using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    /// <summary>Names every module that failed and why, so the thrown error carries the real cause.</summary>
    internal static string DescribeDependencyInstallFailures(string operation, IReadOnlyList<ModuleDependencyInstallResult> failures)
    {
        var details = failures.Select(failure =>
        {
            var version = string.IsNullOrWhiteSpace(failure.RequestedVersion) ? string.Empty : $" {failure.RequestedVersion}";
            var reason = string.IsNullOrWhiteSpace(failure.Message) ? "no error text was reported" : failure.Message!.Trim();
            return $"{failure.Name}{version}: {reason}";
        });
        return $"{operation} failed for {failures.Count} module{(failures.Count == 1 ? string.Empty : "s")}: {string.Join("; ", details)}";
    }
}
