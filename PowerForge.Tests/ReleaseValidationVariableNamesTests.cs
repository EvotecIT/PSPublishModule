namespace PowerForge.Tests;

public sealed class ReleaseValidationVariableNamesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.VariableNames.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationVariableNamesTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false, "_Probe2")]
    [InlineData(true, "_Probe2")]
    [InlineData(false, "Probe_Value2")]
    [InlineData(true, "Probe_Value2")]
    [InlineData(false, "Probe2")]
    [InlineData(true, "Probe2")]
    public async Task Both_public_entrypoints_expand_named_variables_across_command_fields(bool completeRun, string variableName)
    {
        var token = "{" + variableName.ToUpperInvariant() + "}";
        var variables = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["ProjectRoot"] = _root, [variableName] = "payload"
        };
        var originalVariables = variables.ToArray();
        var workingDirectory = Directory.CreateDirectory(Path.Combine(_root, "payload-work")).FullName;
        var runner = new Runner(request => {
            // Model the process boundary: produce a file and output from the actual transformed request.
            File.WriteAllText(Path.Combine(_root, request.Arguments[0] + ".txt"), request.EnvironmentVariables!["PROBE_VALUE"]);
            return request.Arguments[0] + ":" + request.EnvironmentVariables["PROBE_VALUE"] + ":" + request.Arguments[1];
        });
        var command = new ReleaseCommandValidation {
            Name = "Named variable contract", FileName = token + "-exe", WorkingDirectory = token + "-work",
            Arguments = [token, "{\"count\":2}"], Environment = new() { ["PROBE_VALUE"] = token },
            ExpectedOutput = token + ":" + token + ":{\"count\":2}", OutputContains = [token + ":" + token],
            NonEmptyFiles = [token + ".txt"]
        };
        var service = new ReleaseValidationService(runner);

        if (completeRun) {
            var report = await service.RunAsync(new() { Commands = [command] },
                request: new() { ProjectRoot = _root, Variables = variables });
            Assert.True(report.Success, string.Join("; ", report.Errors));
            Assert.Equal(command.Name, Assert.Single(report.Checks));
        } else {
            var result = await service.RunCommandAsync(command, variables);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("payload:payload:{\"count\":2}", result.StdOut);
        }

        var observed = Assert.Single(runner.Requests);
        Assert.Equal("payload-exe", observed.FileName);
        Assert.Equal(workingDirectory, observed.WorkingDirectory);
        Assert.Equal(new[] { "payload", "{\"count\":2}" }, observed.Arguments);
        Assert.Equal("payload", observed.EnvironmentVariables!["PROBE_VALUE"]);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(_root, "payload.txt")));
        Assert.Equal(originalVariables, variables.ToArray());
    }

    [Theory]
    [InlineData("FileName", false)]
    [InlineData("WorkingDirectory", false)]
    [InlineData("Arguments", false)]
    [InlineData("Environment", false)]
    [InlineData("ExpectedOutput", false)]
    [InlineData("OutputContains", false)]
    [InlineData("NonEmptyFiles", false)]
    [InlineData("Arguments", true)]
    public async Task Missing_underscore_variable_is_reported_instead_of_preserved_as_literal(string field, bool completeRun)
    {
        const string token = "{_Missing_Value2}";
        var runner = new Runner(_ => token);
        var command = new ReleaseCommandValidation { Name = "Missing variable", FileName = "probe" };
        switch (field) {
            case "FileName": command.FileName = token; break;
            case "WorkingDirectory": command.WorkingDirectory = token; break;
            case "Arguments": command.Arguments = [token]; break;
            case "Environment": command.Environment["PROBE_VALUE"] = token; break;
            case "ExpectedOutput": command.ExpectedOutput = token; break;
            case "OutputContains": command.OutputContains = [token]; break;
            case "NonEmptyFiles": command.NonEmptyFiles = [token]; break;
            default: throw new ArgumentException("Unknown test field.", nameof(field));
        }
        var service = new ReleaseValidationService(runner);

        string message;
        if (completeRun) {
            var report = await service.RunAsync(new() { Commands = [command] }, request: new() { ProjectRoot = _root });
            Assert.False(report.Success);
            Assert.Empty(report.Checks);
            message = Assert.Single(report.Errors);
        } else {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunCommandAsync(command,
                new Dictionary<string, string> { ["ProjectRoot"] = _root }));
            message = error.Message;
        }

        Assert.Contains("Missing validation variable " + token, message);
        if (field is "ExpectedOutput" or "OutputContains" or "NonEmptyFiles") {
            Assert.Single(runner.Requests);
        } else {
            Assert.Empty(runner.Requests);
        }
    }

    [Theory]
    [InlineData(false, "Package-Root")]
    [InlineData(true, "Package-Root")]
    [InlineData(false, "Package.Root")]
    [InlineData(true, "1Root")]
    [InlineData(false, "Root\n")]
    [InlineData(true, "")]
    public async Task Unsupported_variable_names_fail_at_admission_before_a_probe_runs(bool completeRun, string name)
    {
        var variables = new Dictionary<string, string> { [name] = "value" };
        var runner = new Runner(_ => "unexpected");
        var service = new ReleaseValidationService(runner);
        var command = new ReleaseCommandValidation { FileName = "probe" };
        if (completeRun) {
            var report = await service.RunAsync(new() { Commands = [command] }, request: new() { Variables = variables });
            Assert.False(report.Success);
            Assert.Contains("Invalid validation variable name", Assert.Single(report.Errors));
        } else {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunCommandAsync(command, variables));
            Assert.Contains("Invalid validation variable name", error.Message);
        }
        Assert.Empty(runner.Requests);
    }

    private sealed class Runner(Func<ProcessRunRequest, string> output) : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, output(request), "", request.FileName, TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
