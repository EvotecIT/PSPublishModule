using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

/// <summary>
/// Repository-wide .NET package release workflow (discover, version, pack, publish).
/// </summary>
/// <remarks>
/// <para>
/// Discovers packable projects, resolves a repository-wide version (supports X-pattern),
/// updates csproj versions, packs, and optionally publishes packages.
/// </para>
/// </remarks>
/// <example>
/// <summary>Release packages using X-pattern versioning</summary>
/// <code>Invoke-DotNetRepositoryRelease -Path . -ExpectedVersion '1.2.X' -Publish -PublishApiKey $env:NUGET_API_KEY</code>
/// </example>
/// <example>
/// <summary>Release packages with exclusions and custom sources</summary>
/// <code>Invoke-DotNetRepositoryRelease -Path . -ExpectedVersion '2.0.X' -ExcludeProject 'OfficeIMO.Visio' -NugetSource 'C:\Packages' -Publish -PublishApiKey $env:NUGET_API_KEY</code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "DotNetRepositoryRelease", SupportsShouldProcess = true)]
[OutputType(typeof(DotNetRepositoryReleaseResult))]
public sealed class InvokeDotNetRepositoryReleaseCommand : PSCmdlet
{
    /// <summary>Root repository path.</summary>
    [Parameter]
    public string Path { get; set; } = string.Empty;

    /// <summary>Expected version (exact or X-pattern, e.g. 1.2.X).</summary>
    [Parameter]
    [Alias("Version")]
    public string? ExpectedVersion { get; set; }

    /// <summary>Per-project expected versions (hashtable: ProjectName = Version).</summary>
    [Parameter]
    public IDictionary? ExpectedVersionMap { get; set; }

    /// <summary>When set, only projects listed in ExpectedVersionMap are processed.</summary>
    [Parameter]
    public SwitchParameter ExpectedVersionMapAsInclude { get; set; }

    /// <summary>Allow wildcards (*, ?) in ExpectedVersionMap keys.</summary>
    [Parameter]
    public SwitchParameter ExpectedVersionMapUseWildcards { get; set; }

    /// <summary>Project names to include (csproj file name without extension).</summary>
    [Parameter]
    public string[]? IncludeProject { get; set; }

    /// <summary>Project names to exclude (csproj file name without extension).</summary>
    [Parameter]
    public string[]? ExcludeProject { get; set; }

    /// <summary>Directory names to exclude from discovery.</summary>
    [Parameter]
    public string[]? ExcludeDirectories { get; set; }

    /// <summary>NuGet sources (v3 index or local path) used for version resolution.</summary>
    [Parameter]
    public string[]? NugetSource { get; set; }

    /// <summary>Include prerelease versions when resolving versions.</summary>
    [Parameter]
    public SwitchParameter IncludePrerelease { get; set; }

    /// <summary>Credential username for private NuGet sources.</summary>
    [Parameter]
    public string? NugetCredentialUserName { get; set; }

    /// <summary>Credential secret/token for private NuGet sources.</summary>
    [Parameter]
    public string? NugetCredentialSecret { get; set; }

    /// <summary>Path to a file containing the credential secret/token.</summary>
    [Parameter]
    public string? NugetCredentialSecretFilePath { get; set; }

    /// <summary>Name of environment variable containing the credential secret/token.</summary>
    [Parameter]
    public string? NugetCredentialSecretEnvName { get; set; }

    /// <summary>Build configuration (Release/Debug).</summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string Configuration { get; set; } = "Release";

    /// <summary>Optional output path for packages.</summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>Certificate thumbprint used for signing packages.</summary>
    [Parameter]
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location used when searching for the signing certificate.</summary>
    [Parameter]
    public CertificateStoreLocation CertificateStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Timestamp server URL used while signing packages.</summary>
    [Parameter]
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Skip dotnet pack step.</summary>
    [Parameter]
    public SwitchParameter SkipPack { get; set; }

    /// <summary>Publish packages to the feed.</summary>
    [Parameter]
    public SwitchParameter Publish { get; set; }

    /// <summary>NuGet feed source for publishing.</summary>
    [Parameter]
    public string? PublishSource { get; set; }

    /// <summary>API key used for publishing packages.</summary>
    [Parameter]
    public string? PublishApiKey { get; set; }

    /// <summary>Path to a file containing the publish API key.</summary>
    [Parameter]
    public string? PublishApiKeyFilePath { get; set; }

    /// <summary>Name of environment variable containing the publish API key.</summary>
    [Parameter]
    public string? PublishApiKeyEnvName { get; set; }

    /// <summary>Skip duplicates when pushing packages.</summary>
    [Parameter]
    public SwitchParameter SkipDuplicate { get; set; }

    /// <summary>Stop on the first publish/signing failure.</summary>
    [Parameter]
    public SwitchParameter PublishFailFast { get; set; }

    /// <summary>Executes repository release workflow.</summary>
    protected override void ProcessRecord()
    {
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        ConsoleEncoding.EnsureUtf8();
        try
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                AnsiConsole.Profile.Capabilities.Unicode = true;
        }
        catch
        {
            // best effort only
        }

        var interactive = SpectrePipelineConsoleUi.ShouldUseInteractiveView(isVerbose);
        ILogger logger = interactive
            ? new SpectreConsoleLogger { IsVerbose = isVerbose }
            : new CmdletLogger(this, isVerbose);
        var currentPath = SessionState.Path.CurrentFileSystemLocation.Path;
        var preparation = new DotNetRepositoryReleasePreparationService().Prepare(new DotNetRepositoryReleasePreparationRequest
        {
            CurrentPath = currentPath,
            RootPath = Path,
            ExpectedVersion = ExpectedVersion,
            ExpectedVersionMap = ExpectedVersionMap,
            ExpectedVersionMapAsInclude = ExpectedVersionMapAsInclude.IsPresent,
            ExpectedVersionMapUseWildcards = ExpectedVersionMapUseWildcards.IsPresent,
            IncludeProject = IncludeProject,
            ExcludeProject = ExcludeProject,
            ExcludeDirectories = ExcludeDirectories,
            NugetSource = NugetSource,
            IncludePrerelease = IncludePrerelease.IsPresent,
            NugetCredentialUserName = NugetCredentialUserName,
            NugetCredentialSecret = NugetCredentialSecret,
            NugetCredentialSecretFilePath = NugetCredentialSecretFilePath,
            NugetCredentialSecretEnvName = NugetCredentialSecretEnvName,
            Configuration = Configuration,
            OutputPath = OutputPath,
            CertificateThumbprint = CertificateThumbprint,
            CertificateStore = CertificateStore == CertificateStoreLocation.LocalMachine
                ? PowerForge.CertificateStoreLocation.LocalMachine
                : PowerForge.CertificateStoreLocation.CurrentUser,
            TimeStampServer = TimeStampServer,
            SkipPack = SkipPack.IsPresent,
            Publish = Publish.IsPresent,
            PublishSource = PublishSource,
            PublishApiKey = PublishApiKey,
            PublishApiKeyFilePath = PublishApiKeyFilePath,
            PublishApiKeyEnvName = PublishApiKeyEnvName,
            SkipDuplicate = SkipDuplicate.IsPresent,
            PublishFailFast = PublishFailFast.IsPresent
        });

        var executeBuild = ShouldProcess(preparation.RootPath, "Release .NET repository packages");
        var result = new DotNetRepositoryReleaseWorkflowService(logger).Execute(preparation, executeBuild);
        if (interactive)
            WriteRepositorySummary(result, isPlan: !executeBuild);
        WriteObject(result);
    }

    private static void WriteRepositorySummary(DotNetRepositoryReleaseResult result, bool isPlan)
    {
        if (result is null) return;

        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;
        var title = isPlan ? "Plan" : "Summary";
        var icon = unicode ? "✅" : "*";

        AnsiConsole.Write(new Rule($"[green]{icon} {title}[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Project").NoWrap())
            .AddColumn(new TableColumn("Packable").NoWrap())
            .AddColumn(new TableColumn("Version").NoWrap())
            .AddColumn(new TableColumn("Packages").NoWrap())
            .AddColumn(new TableColumn("Status").NoWrap())
            .AddColumn(new TableColumn("Error"));

        foreach (var project in result.Projects.OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            var packable = project.IsPackable ? "Yes" : "No";
            var version = string.IsNullOrWhiteSpace(project.OldVersion) && string.IsNullOrWhiteSpace(project.NewVersion)
                ? string.Empty
                : $"{project.OldVersion ?? "?"} -> {project.NewVersion ?? "?"}";
            var packages = project.Packages.Count.ToString();
            var status = string.IsNullOrWhiteSpace(project.ErrorMessage)
                ? (project.IsPackable ? "[green]Ok[/]" : "[grey]Skipped[/]")
                : "[red]Fail[/]";
            var error = TrimForTable(project.ErrorMessage);

            table.AddRow(Esc(project.ProjectName), packable, Esc(version), packages, status, Esc(error));
        }

        AnsiConsole.Write(table);

        var totals = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        var totalProjects = result.Projects.Count;
        var packableProjects = result.Projects.Count(p => p.IsPackable);
        var failedProjects = result.Projects.Count(p => !string.IsNullOrWhiteSpace(p.ErrorMessage));
        var totalPackages = result.Projects.Sum(p => p.Packages.Count);

        totals.AddRow("Projects", totalProjects.ToString());
        totals.AddRow("Packable", packableProjects.ToString());
        totals.AddRow("Failed", failedProjects.ToString());
        totals.AddRow("Packages", totalPackages.ToString());
        if (result.PublishedPackages.Count > 0)
            totals.AddRow("Published", result.PublishedPackages.Count.ToString());
        if (result.SkippedDuplicatePackages.Count > 0)
            totals.AddRow("Skipped duplicates", result.SkippedDuplicatePackages.Count.ToString());
        if (result.FailedPackages.Count > 0)
            totals.AddRow("Failed publishes", result.FailedPackages.Count.ToString());
        if (!string.IsNullOrWhiteSpace(result.ResolvedVersion))
            totals.AddRow("Resolved version", Esc(result.ResolvedVersion));

        AnsiConsole.Write(totals);
    }

    private static string TrimForTable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value!.Trim();
        const int max = 140;
        if (trimmed.Length <= max) return trimmed;
        return trimmed.Substring(0, max - 3) + "...";
    }

}
