using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Resolves registered PowerShell repository metadata using PSResourceGet or PowerShellGet.
/// </summary>
public sealed class PowerShellRepositoryResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPowerShellRunner _powerShellRunner;

    /// <summary>
    /// Creates a new resolver using the default PowerShell runner.
    /// </summary>
    public PowerShellRepositoryResolver()
        : this(new PowerShellRunner())
    {
    }

    internal PowerShellRepositoryResolver(IPowerShellRunner powerShellRunner)
    {
        _powerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
    }

    /// <summary>
    /// Resolves repository metadata for the provided repository name or URI.
    /// </summary>
    public async Task<PowerShellRepositoryResolution?> ResolveAsync(string workingDirectory, string repositoryName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);

        if (Uri.TryCreate(repositoryName, UriKind.Absolute, out var directUri))
        {
            return new PowerShellRepositoryResolution {
                Name = repositoryName,
                SourceUri = directUri.AbsoluteUri
            };
        }

        var script = string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"$name = {QuoteLiteral(repositoryName)}",
            "$psResourceRepo = Get-Command -Name Get-PSResourceRepository -ErrorAction SilentlyContinue",
            "if ($null -ne $psResourceRepo) {",
            "  $repo = Get-PSResourceRepository -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1",
            "  if ($null -ne $repo) {",
            "    @{ Name = $repo.Name; SourceUri = $repo.Uri; PublishUri = $repo.PublishUri } | ConvertTo-Json -Compress",
            "    exit 0",
            "  }",
            "}",
            "$psRepo = Get-PSRepository -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -ne $psRepo) {",
            "  @{ Name = $psRepo.Name; SourceUri = $psRepo.SourceLocation; PublishUri = $psRepo.PublishLocation } | ConvertTo-Json -Compress",
            "  exit 0",
            "}",
            "exit 1"
        });

        var startedAt = Stopwatch.StartNew();
        var result = await Task.Run(() => _powerShellRunner.Run(PowerShellRunRequest.ForCommand(
            commandText: script,
            timeout: TimeSpan.FromMinutes(2),
            preferPwsh: !OperatingSystem.IsWindows(),
            workingDirectory: workingDirectory,
            executableOverride: Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE"))), cancellationToken).ConfigureAwait(false);
        startedAt.Stop();

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PowerShellRepositoryResolution>(result.StdOut.Trim(), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string QuoteLiteral(string value)
        => $"'{(value ?? string.Empty).Replace("'", "''")}'";
}
