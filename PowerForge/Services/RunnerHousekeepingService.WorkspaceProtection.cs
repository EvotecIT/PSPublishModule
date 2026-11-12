using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private static string[] ResolveProtectedWorkspaceNames(Dictionary<string, string[]>? workspaces)
    {
        string? localProtection = Environment.GetEnvironmentVariable("POWERFORGE_RUNNER_PROTECTED_WORKSPACES");
        string[] localNames = string.IsNullOrWhiteSpace(localProtection)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(localProtection!)
              ?? throw new ArgumentException("Machine-local protected workspace names cannot be null.");
        string[] names = (workspaces ?? new Dictionary<string, string[]>())
            .Where(entry => string.Equals(entry.Key, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry => entry.Value ?? throw new ArgumentException("Protected workspace names cannot be null."))
            .Concat(localNames)
            .ToArray();

        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
                name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Protected workspaces must be top-level directory names.");
            }
        }

        return names.Distinct(GetPathStringComparer()).ToArray();
    }
}
