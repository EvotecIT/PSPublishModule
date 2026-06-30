namespace PowerForge;

internal sealed class ManagedModuleNativePowerShellRunner : IPowerShellRunner
{
    private readonly IPowerShellRunner _inner;
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
    private readonly string _workingDirectory;
    private readonly bool _preferPwsh;
    private readonly string? _executableOverride;

    internal ManagedModuleNativePowerShellRunner(
        IPowerShellRunner inner,
        IReadOnlyDictionary<string, string?> environmentVariables,
        string workingDirectory,
        bool preferPwsh,
        string? executableOverride)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _environmentVariables = environmentVariables ?? throw new ArgumentNullException(nameof(environmentVariables));
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? throw new ArgumentException("Working directory is required.", nameof(workingDirectory))
            : workingDirectory;
        _preferPwsh = preferPwsh;
        _executableOverride = executableOverride;
    }

    public PowerShellRunResult Run(PowerShellRunRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var environment = MergeEnvironment(request.EnvironmentVariables);
        return _inner.Run(CloneRequest(request, environment));
    }

    private Dictionary<string, string?> MergeEnvironment(IReadOnlyDictionary<string, string?>? requestEnvironment)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in _environmentVariables)
        {
            merged[variable.Key] = variable.Value;
        }

        if (requestEnvironment is not null)
        {
            foreach (var variable in requestEnvironment)
            {
                merged[variable.Key] = variable.Value;
            }
        }

        return merged;
    }

    private PowerShellRunRequest CloneRequest(
        PowerShellRunRequest request,
        IReadOnlyDictionary<string, string?> environment)
    {
        if (request.InvocationMode == PowerShellInvocationMode.Command)
        {
            return PowerShellRunRequest.ForCommand(
                request.CommandText ?? string.Empty,
                request.Timeout,
                _preferPwsh,
                _workingDirectory,
                environment,
                _executableOverride,
                request.CaptureOutput,
                request.CaptureError);
        }

        return new PowerShellRunRequest(
            request.ScriptPath ?? string.Empty,
            request.Arguments,
            request.Timeout,
            _preferPwsh,
            _workingDirectory,
            environment,
            _executableOverride,
            request.CaptureOutput,
            request.CaptureError);
    }
}
