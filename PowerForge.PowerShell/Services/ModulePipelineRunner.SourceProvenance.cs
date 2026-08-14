using System;
using System.Diagnostics;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal static (string? Revision, bool Dirty) ReadModuleSourceProvenance(string projectRoot)
    {
        string? repositoryRoot = ReadGitValue(projectRoot, "rev-parse --show-toplevel");
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            return (null, false);

        string? revision = ReadGitValue(repositoryRoot!, "rev-parse HEAD");
        if (string.IsNullOrWhiteSpace(revision))
            return (null, false);
        revision = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            revision,
            "module source revision");
        string? status = ReadGitValue(repositoryRoot!, "status --porcelain=v1 --untracked-files=normal", allowEmpty: true);
        if (status is null)
            throw new InvalidOperationException("Unable to inspect the module source Git status before signing.");
        return (revision, status.Length > 0);
    }

    private static string? ReadGitValue(string workingDirectory, string arguments, bool allowEmpty = false)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start())
                return null;
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("Git provenance inspection timed out.");
            }
            if (process.ExitCode != 0)
                return null;
            string normalized = output.Trim();
            return allowEmpty || normalized.Length > 0 ? normalized : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
