using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;

namespace PSPublishModule;

/// <summary>
/// Pushes NuGet packages to a feed using <c>dotnet nuget push</c>.
/// </summary>
[Cmdlet(VerbsData.Publish, "NugetPackage", SupportsShouldProcess = true)]
public sealed class PublishNugetPackageCommand : PSCmdlet
{
    /// <summary>Directory to search for NuGet packages.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] Path { get; set; } = Array.Empty<string>();

    /// <summary>API key used to authenticate against the NuGet feed.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>NuGet feed URL.</summary>
    [Parameter]
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// When set, passes <c>--skip-duplicate</c> to <c>dotnet nuget push</c>.
    /// This makes repeated publishing runs idempotent when the package already exists.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipDuplicate { get; set; }

    /// <summary>Executes publishing.</summary>
    protected override void ProcessRecord()
    {
        var result = new PublishNugetPackageResult();

        try
        {
            var resolvedRoots = new List<string>();
            foreach (var raw in Path ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(raw);
                if (!Directory.Exists(root))
                {
                    result.Success = false;
                    result.ErrorMessage = $"Path '{raw}' not found.";
                    WriteObject(result);
                    return;
                }
                resolvedRoots.Add(root);
            }

            if (resolvedRoots.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No paths were provided.";
                WriteObject(result);
                return;
            }

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packages = resolvedRoots
                .SelectMany(r => Directory.EnumerateFiles(r, "*.nupkg", SearchOption.AllDirectories))
                .Where(p => unique.Add(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var foundAny = false;

            foreach (var pkg in packages)
            {
                foundAny = true;
                if (ShouldProcess(pkg, $"Publish NuGet package to {Source}"))
                {
                    var exitCode = RunDotnetNugetPush(pkg, ApiKey, Source, SkipDuplicate.IsPresent, out var stdErr, out var stdOut);
                    if (exitCode == 0)
                    {
                        result.Pushed.Add(pkg);
                    }
                    else
                    {
                        result.Failed.Add(pkg);
                        result.Success = false;
                        WriteVerbose($"dotnet nuget push failed for {pkg} (exit {exitCode}).");
                        if (!string.IsNullOrWhiteSpace(stdErr))
                            WriteVerbose(stdErr.Trim());
                        if (!string.IsNullOrWhiteSpace(stdOut))
                            WriteVerbose(stdOut.Trim());
                    }
                }
                else
                {
                    // WhatIf mode in legacy function simulated success by adding to Pushed.
                    result.Pushed.Add(pkg);
                }
            }

            if (!foundAny)
            {
                result.Success = false;
                var display = string.Join(", ", (Path ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)));
                result.ErrorMessage = $"No packages found in {display}";
            }

            WriteObject(result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            WriteObject(result);
        }
    }

    private static int RunDotnetNugetPush(string packagePath, string apiKey, string source, bool skipDuplicate, out string stdErr, out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
#if NET472
        var args = new List<string>
        {
            "nuget", "push", packagePath,
            "--api-key", apiKey,
            "--source", source
        };
        if (skipDuplicate) args.Add("--skip-duplicate");
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add(apiKey);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(source);
        if (skipDuplicate) psi.ArgumentList.Add("--skip-duplicate");
#endif

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    // Based on .NET's internal ProcessStartInfo quoting behavior for Windows CreateProcess.
    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null) return "\"\"";
        if (arg.Length == 0) return "\"\"";

        bool needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes) return arg;

        var sb = new StringBuilder();
        sb.Append('"');

        int backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif

    /// <summary>Result returned by <c>Publish-NugetPackage</c>.</summary>
    public sealed class PublishNugetPackageResult
    {
        /// <summary>Whether all packages were pushed successfully.</summary>
        public bool Success { get; set; } = true;

        /// <summary>List of packages pushed (or simulated in WhatIf).</summary>
        public List<string> Pushed { get; } = new();

        /// <summary>List of packages that failed to push.</summary>
        public List<string> Failed { get; } = new();

        /// <summary>Optional error message for overall failure (e.g., path not found).</summary>
        public string? ErrorMessage { get; set; }
    }
}
