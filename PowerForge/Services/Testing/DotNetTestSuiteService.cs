using System.Text;

namespace PowerForge;

/// <summary>Creates conventional .NET test steps and delegates execution to workspace validation.</summary>
public sealed class DotNetTestSuiteService
{
    private readonly WorkspaceValidationService _validation;

    /// <summary>Creates the service with the existing process execution boundary.</summary>
    public DotNetTestSuiteService(IProcessRunner? runner = null)
        => _validation = new WorkspaceValidationService(runner ?? new ProcessRunner(ownProcessTree: true));

    /// <summary>Creates a build/test/native plan without running processes or touching outputs.</summary>
    public WorkspaceValidationSpec Plan(DotNetTestSuiteSpec spec, string configPath, string profileName,
        string? framework, string? runtimeIdentifier, string resultsPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var profile = spec.Profiles.SingleOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Test profile '{profileName}' was not found.");
        if (profile.TimeoutSeconds <= 0) throw new ArgumentException("TimeoutSeconds must be positive.");
        var root = PathValueResolver.Resolve(Path.GetDirectoryName(Path.GetFullPath(configPath))!, spec.ProjectRoot);
        var results = PathValueResolver.Resolve(root, resultsPath);
        var steps = new List<WorkspaceValidationStep>();

        void Add(string id, IEnumerable<string> arguments, string? executable = null,
            Dictionary<string, string?>? environment = null)
        {
            var variables = new Dictionary<string, string?>(profile.EnvironmentVariables)
            {
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
            };
            if (environment is not null)
                foreach (var entry in environment) variables[entry.Key] = entry.Value;
            steps.Add(new WorkspaceValidationStep
            {
                Id = id, Name = id, Kind = executable is null ? WorkspaceValidationStepKind.DotNet : WorkspaceValidationStepKind.Command,
                Executable = executable, Arguments = arguments.ToArray(), EnvironmentVariables = variables,
                TimeoutSeconds = profile.TimeoutSeconds
            });
        }

        string Project(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A test/build project path is empty.");
            return PathValueResolver.Resolve(root, path);
        }

        void Build(string path, string? target, string id)
        {
            var args = new List<string> { "build", Project(path), "--configuration", profile.Configuration, "--nologo", "-nodeReuse:false" };
            if (!string.IsNullOrWhiteSpace(target)) args.AddRange(new[] { "--framework", target! });
            if (profile.WarningsAsErrors) args.Add("-warnaserror");
            Add(id, args);
        }

        void Probes(WorkspaceValidationStep[] probes, string phase)
        {
            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                if (string.IsNullOrWhiteSpace(probe.Executable) && probe.Kind == WorkspaceValidationStepKind.Command)
                    throw new ArgumentException($"Probe {phase}-{i} requires an executable.");
                var environment = new Dictionary<string, string?>(profile.EnvironmentVariables);
                foreach (var entry in probe.EnvironmentVariables) environment[entry.Key] = entry.Value;
                steps.Add(new WorkspaceValidationStep
                {
                    Id = $"{phase}-{i}", Name = probe.Name ?? probe.Id, Kind = probe.Kind, Executable = probe.Executable,
                    Arguments = probe.Arguments, WorkingDirectory = probe.WorkingDirectory, EnvironmentVariables = environment,
                    Frameworks = probe.Frameworks.Length > 0 ? probe.Frameworks : string.IsNullOrWhiteSpace(framework) ? Array.Empty<string>() : new[] { framework! },
                    Items = probe.Items, Profiles = probe.Profiles, RequiredFeatures = probe.RequiredFeatures,
                    RequiredPath = probe.RequiredPath, ContinueOnMissingRequiredPath = probe.ContinueOnMissingRequiredPath,
                    TimeoutSeconds = profile.TimeoutSeconds, FailureContext = probe.FailureContext, FailureHint = probe.FailureHint
                });
            }
        }

        for (var i = 0; i < profile.BuildProjects.Length; i++)
        {
            var project = profile.BuildProjects[i];
            var targets = project.Frameworks.Length == 0 ? new[] { framework } : project.Frameworks;
            for (var j = 0; j < targets.Length; j++) Build(project.Path, targets[j], $"build-{i}-{j}");
        }
        for (var i = 0; i < profile.TestProjects.Length; i++) Build(profile.TestProjects[i], framework, $"test-build-{i}");
        Probes(profile.BeforeTests, "before");
        for (var i = 0; i < profile.TestProjects.Length; i++)
        {
            var args = new List<string> { "test", Project(profile.TestProjects[i]), "--configuration", profile.Configuration,
                "--no-build", "--verbosity", "minimal", "--logger", "console;verbosity=minimal", "--logger", $"trx;LogFilePrefix=tests-{i}",
                "--results-directory", Path.Combine(results, $"project-{i}"), "-nodeReuse:false" };
            if (!string.IsNullOrWhiteSpace(framework)) args.AddRange(new[] { "--framework", framework! });
            Add($"test-{i}", args);
        }
        for (var i = 0; i < profile.NativeProjects.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtimeIdentifier))
                throw new ArgumentException("Native smoke tests require a framework and runtime identifier.");
            var native = profile.NativeProjects[i];
            if (string.IsNullOrWhiteSpace(native.Executable) || Path.GetFileName(native.Executable) != native.Executable ||
                native.Executable.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Native Executable must be a filename.");
            var output = Path.Combine(results, $"native-{i}");
            var args = new List<string> { "publish", Project(native.Path), "-c", profile.Configuration, "-f", framework!,
                "-r", runtimeIdentifier!, "-o", output, "-p:PublishAot=true", "-p:IlcTreatWarningsAsErrors=true",
                "-p:ILLinkTreatWarningsAsErrors=true", "-nodeReuse:false" };
            if (profile.WarningsAsErrors) args.Add("-warnaserror");
            Add($"publish-{i}", args);
            var environments = native.RunEnvironments.Length == 0 ? new[] { new Dictionary<string, string?>() } : native.RunEnvironments;
            for (var j = 0; j < environments.Length; j++)
                Add($"native-run-{i}-{j}", native.Arguments, Path.Combine(output, native.Executable), environments[j]);
        }
        Probes(profile.AfterTests, "after");
        if (steps.Count == 0) throw new ArgumentException($"Test profile '{profileName}' contains no builds, tests or probes.");
        return new WorkspaceValidationSpec
        {
            ProjectRoot = root, Profiles = new[] { new WorkspaceValidationProfile { Name = profileName } },
            Variables = new Dictionary<string, string?> { ["framework"] = framework, ["runtime"] = runtimeIdentifier,
                ["resultsPath"] = results, ["configuration"] = profile.Configuration },
            Steps = steps.ToArray()
        };
    }

    /// <summary>Runs the plan and retains a Markdown summary even when a process fails.</summary>
    public async Task<WorkspaceValidationResult> RunAsync(DotNetTestSuiteSpec spec, string configPath, string profileName,
        string? framework, string? runtimeIdentifier, string resultsPath, CancellationToken cancellationToken = default)
    {
        var plan = Plan(spec, configPath, profileName, framework, runtimeIdentifier, resultsPath);
        var output = plan.Variables["resultsPath"]!;
        Directory.CreateDirectory(output);
        var request = new WorkspaceValidationRequest { ProfileName = profileName, CaptureOutput = false, CaptureError = false };
        WorkspaceValidationResult? result = null;
        try
        {
            result = await _validation.RunAsync(plan, configPath, request, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            var text = new StringBuilder("## .NET tests\n\n");
            text.AppendLine($"Profile: `{profileName}`; framework: `{framework ?? "project default"}`; runtime: `{runtimeIdentifier ?? "default"}`.\n");
            text.AppendLine(result?.Succeeded == true ? "All steps passed.\n" : "Validation failed or was interrupted. See the job log.\n");
            foreach (var step in result?.Steps ?? Array.Empty<WorkspaceValidationStepResult>())
                text.AppendLine($"- {(step.Succeeded ? "PASS" : "FAIL")} `{step.Step.Name}` (exit {step.ExitCode})");
            File.WriteAllText(Path.Combine(output, "summary.md"), text.ToString(), new UTF8Encoding(false));
        }
    }
}
