using System.Diagnostics;

namespace PowerForgeStudio.Orchestrator.Portfolio;

internal sealed class GitRemoteResolver : IGitRemoteResolver
{
    public async Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
        {
            return null;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo {
                FileName = "git",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("remote");
            process.StartInfo.ArgumentList.Add("get-url");
            process.StartInfo.ArgumentList.Add("origin");

            process.Start();
            var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var _ = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0
                ? standardOutput.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
