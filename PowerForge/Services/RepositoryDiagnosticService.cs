using System.Text;
using System.Text.Json;
using System.Threading;

namespace PowerForge;

/// <summary>Collects read-only repository diagnostics and explains publish failures without exposing secrets.</summary>
public sealed class RepositoryDiagnosticService
{
    internal const string Marker = "PFREPO::DIAGNOSTIC::";
    private readonly IPowerShellRunner _runner;

    /// <summary>Creates a service using the standard isolated PowerShell process runner.</summary>
    public RepositoryDiagnosticService() : this(new PowerShellRunner()) { }

    /// <summary>Creates a service using the supplied PowerShell host boundary.</summary>
    public RepositoryDiagnosticService(IPowerShellRunner runner)
        => _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>Inspects local registration and settings. Does not register, log in, decrypt, or contact a feed.</summary>
    public RepositoryDiagnosticReport Inspect(RepositoryDiagnosticRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Tool != PublishTool.PSResourceGet && request.Tool != PublishTool.PowerShellGet)
            throw new ArgumentException("Select PSResourceGet or PowerShellGet for native repository inspection.", nameof(request));
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerForge", "repository-diagnostics");
        Directory.CreateDirectory(root);
        var path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(path, Compose(EmbeddedScripts.Load("Scripts/RepositoryDiagnostics/Inspect-Repository.ps1")), new UTF8Encoding(true));
        try
        {
            var run = new PowerShellRunRequest(path,
                new[] { request.RepositoryName, request.Tool.ToString(), request.RepositoryUri ?? string.Empty },
                TimeSpan.FromSeconds(45), workingDirectory: request.WorkingDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            var result = _runner is ICancellablePowerShellRunner cancellable
                ? cancellable.RunAsync(run, cancellationToken).GetAwaiter().GetResult() : _runner.Run(run);
            cancellationToken.ThrowIfCancellationRequested();
            var report = Parse(result.StdOut, request.Tool, request.RepositoryName);
            report.ConfiguredUri = RepositoryDiagnosticRedactor.Endpoint(request.RepositoryUri);
            if (result.ExitCode != 0) Add(report, "PFREPO005", "Warning", "The inspection process did not complete.", "Check PowerShell availability and publisher module installation.");
            Complete(report);
            return report;
        }
        finally { try { File.Delete(path); } catch { /* Diagnostic scratch must not mask the inspection result. */ } }
    }

    internal static string Compose(string script)
        => script.Replace("# POWERFORGE_REPOSITORY_DIAGNOSTICS", EmbeddedScripts.Load("Scripts/RepositoryDiagnostics/Collect-RepositoryDiagnostic.ps1"));

    internal static RepositoryDiagnosticReport Parse(string stdout, PublishTool tool, string? repository)
    {
        RepositoryDiagnosticReport? report = null;
        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith(Marker, StringComparison.Ordinal) || line.Length > 1024 * 1024) continue;
            try { report = JsonSerializer.Deserialize<RepositoryDiagnosticReport>(Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(Marker.Length)))); }
            catch (Exception ex) when (ex is FormatException || ex is JsonException) { }
        }
        report ??= new RepositoryDiagnosticReport();
        report.Runtime ??= new RepositoryDiagnosticRuntime();
        report.AuthenticationInputs ??= Array.Empty<RepositoryDiagnosticInput>();
        report.ConfigurationFiles ??= Array.Empty<RepositoryDiagnosticFile>();
        report.Findings ??= Array.Empty<RepositoryDiagnosticFinding>();
        report.Tool = tool.ToString();
        report.RepositoryName = repository ?? "PSGallery";
        return report;
    }

    internal static RepositoryPublishDiagnosticException PublishFailure(PublishTool tool, int exitCode, string? message,
        string stdout, string? repository, string packagePath, string? apiKey, RepositoryCredential? credential, string? apiKeySource = null)
    {
        var encodedBasic = credential is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Secret));
        var redactor = new RepositoryDiagnosticRedactor(apiKey, credential?.Secret, credential?.UserName, encodedBasic);
        var report = PublishReport(tool, stdout, repository, packagePath, apiKey, credential, apiKeySource);
        var safeMessage = redactor.Text(message);
        var summary = safeMessage.IndexOf("401", StringComparison.Ordinal) >= 0
            ? "Repository authentication was rejected (HTTP 401)." : "Repository publishing failed.";
        var findings = string.Join(" ", report.Findings.Select(f => f.Message));
        return new RepositoryPublishDiagnosticException(
            $"{summary} {tool} exited with code {exitCode}. {safeMessage} {findings} Inspect local settings with Get-ModuleRepositoryDiagnostic -RepositoryName '<repository>' -Tool {tool}.", report);
    }

    internal static RepositoryDiagnosticReport PublishReport(PublishTool tool, string stdout, string? repository,
        string packagePath, string? apiKey, RepositoryCredential? credential, string? apiKeySource = null)
    {
        var encodedBasic = credential is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Secret));
        var redactor = new RepositoryDiagnosticRedactor(apiKey, credential?.Secret, credential?.UserName, encodedBasic);
        var report = Parse(stdout, tool, repository);
        report.Operation = "Publish";
        report.CollectionPhase = "BeforePublish";
        report.PackagePath = packagePath;
        report.CredentialSupplied = !string.IsNullOrWhiteSpace(credential?.UserName) && !string.IsNullOrWhiteSpace(credential?.Secret);
        report.CredentialSource = report.CredentialSupplied == true ? credential?.Source ?? "Supplied for this invocation" : "Not supplied";
        report.ApiKeySupplied = !string.IsNullOrWhiteSpace(apiKey);
        report.ApiKeySource = report.ApiKeySupplied == true ? apiKeySource ?? "Supplied for this invocation" : "Not supplied";
        Complete(report, redactor);
        return report;
    }

    internal static void WriteSummary(RepositoryDiagnosticReport report, ILogger logger)
    {
        if (logger.IsVerbose) logger.Verbose($"Repository publisher: {report.Tool} {report.Runtime.ModuleVersion}; PowerShell {report.Runtime.PowerShellVersion}; NuGet {report.Runtime.NuGetVersion}. Authentication selection: {report.AuthenticationSelection}.");
        foreach (var finding in report.Findings.Where(f => f.Severity == "Warning")) logger.Warn(finding.Message);
    }

    internal static void Complete(RepositoryDiagnosticReport report, RepositoryDiagnosticRedactor? redactor = null)
    {
        redactor ??= new RepositoryDiagnosticRedactor();
        if (!report.Runtime.ModuleAvailable) Add(report, "PFREPO004", "Warning", "The publisher module could not be imported, or its runtime metadata was unavailable.", "Check the publisher module in the child PowerShell environment.");
        if (report.Runtime.SettingsInspection != "Complete") Add(report, "PFREPO003", "Info", "Settings inspection is incomplete; missing evidence does not mean no saved credentials exist.", "Review configuration file status and the detected backend version.");
        if (report.ApiKeySupplied != true && report.AuthenticationInputs.Any(i => i.Kind == "NuGetApiKey" && i.Present == true && i.Effective != false && i.Match == "ExactEndpoint"))
            Add(report, "PFREPO001", "Warning", "A saved NuGet API key matches the repository endpoint and may affect upload authentication.", "Review the matching API key entry and any explicitly supplied API key before changing repository credentials.");
        if (report.AuthenticationInputs.Any(i => i.Kind == "EnvironmentCredential" && i.Present == true && i.Match == "SourceName"))
            Add(report, "PFREPO002", "Warning", "A NuGet source credential environment variable is present and may affect authentication.", "Review this variable in the publishing process; its value is intentionally omitted.");
        if (report.RepositoryFound == false) Add(report, "PFREPO006", "Warning", "The repository is not registered with the selected backend.", "Check the repository name, backend, and execution identity.");
        report.RepositoryName = redactor.Text(report.RepositoryName);
        report.PackagePath = redactor.Text(report.PackagePath);
        report.CredentialSource = redactor.Text(report.CredentialSource);
        report.ApiKeySource = redactor.Text(report.ApiKeySource);
        report.ConfiguredUri = redactor.Text(report.ConfiguredUri);
        report.RegisteredUri = redactor.Text(report.RegisteredUri);
        report.PublishSourceUri = redactor.Text(report.PublishSourceUri);
        if (!string.IsNullOrEmpty(report.ConfiguredUri) && !string.IsNullOrEmpty(report.RegisteredUri) && report.ConfiguredUri != report.RegisteredUri)
            Add(report, "PFREPO007", "Warning", "The configured endpoint differs from the endpoint registered with the selected backend.", "Compare source and upload settings with the repository registration.");
        report.Runtime.Executable = redactor.Text(report.Runtime.Executable);
        report.Runtime.ModulePath = redactor.Text(report.Runtime.ModulePath);
        report.Runtime.WorkingDirectory = redactor.Text(report.Runtime.WorkingDirectory);
        foreach (var file in report.ConfigurationFiles) file.Path = redactor.Text(file.Path);
        foreach (var input in report.AuthenticationInputs) { input.Name = redactor.Text(input.Name); input.Location = redactor.Text(input.Location); }
    }

    private static void Add(RepositoryDiagnosticReport report, string code, string severity, string message, string action)
    {
        if (report.Findings.Any(f => f.Code == code)) return;
        report.Findings = report.Findings.Concat(new[] { new RepositoryDiagnosticFinding { Code = code, Severity = severity, Message = message, RecommendedAction = action } }).ToArray();
    }
}
