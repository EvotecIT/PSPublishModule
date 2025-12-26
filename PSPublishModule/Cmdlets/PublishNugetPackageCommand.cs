using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    public string Path { get; set; } = string.Empty;

    /// <summary>API key used to authenticate against the NuGet feed.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>NuGet feed URL.</summary>
    [Parameter]
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>Executes publishing.</summary>
    protected override void ProcessRecord()
    {
        var result = new PublishNugetPackageResult();

        try
        {
            var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
            if (!Directory.Exists(root))
            {
                result.Success = false;
                result.ErrorMessage = $"Path '{Path}' not found.";
                WriteObject(result);
                return;
            }

            var packages = Directory.EnumerateFiles(root, "*.nupkg", SearchOption.AllDirectories);
            var foundAny = false;

            foreach (var pkg in packages)
            {
                foundAny = true;
                if (ShouldProcess(pkg, $"Publish NuGet package to {Source}"))
                {
                    var exitCode = RunDotnetNugetPush(pkg, ApiKey, Source, out var stdErr, out var stdOut);
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
                result.ErrorMessage = $"No packages found in {Path}";
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

    private static int RunDotnetNugetPush(string packagePath, string apiKey, string source, out string stdErr, out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"nuget push {Quote(packagePath)} --api-key {Quote(apiKey)} --source {Quote(source)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (!value.Contains(" ") && !value.Contains("\"")) return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

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
