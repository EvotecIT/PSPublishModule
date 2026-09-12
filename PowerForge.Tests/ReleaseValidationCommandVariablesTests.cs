namespace PowerForge.Tests;

public sealed class ReleaseValidationCommandVariablesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.CommandVariables.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationCommandVariablesTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("custom")]
    public async Task Public_command_keeps_default_working_directory_with_optional_custom_variables(string variation)
    {
        Dictionary<string, string>? variables = variation == "null" ? null : new(StringComparer.Ordinal);
        if (variation == "custom") { variables!["Version"] = "1.2.3"; }
        var original = variables?.ToArray();
        var command = Shell(OperatingSystem.IsWindows() ? "cd" : "pwd");
        if (variation == "custom")
        {
            command.Environment["RELEASE_VARIABLE_PROBE"] = "{vErSiOn}";
            command.Arguments = OperatingSystem.IsWindows()
                ? ["/d", "/c", "cd & echo %RELEASE_VARIABLE_PROBE%"]
                : ["-c", "pwd; printf '%s\\n' \"$RELEASE_VARIABLE_PROBE\""];
            command.OutputContains = ["{VERSION}"];
        }

        var result = await new ReleaseValidationService().RunCommandAsync(command, variables);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        var lines = result.StdOut.Trim().Split('\n', StringSplitOptions.TrimEntries);
        Assert.Equal(Path.GetFullPath(Environment.CurrentDirectory), Path.GetFullPath(lines[0]),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Assert.Equal(variation == "custom" ? 2 : 1, lines.Length);
        if (variation == "custom") { Assert.Equal("1.2.3", lines[1]); }
        if (variables is not null)
        {
            Assert.Equal(original, variables.ToArray());
            Assert.False(variables.ContainsKey("ProjectRoot"));
        }
    }

    [Fact]
    public async Task Explicit_project_root_and_tokens_ignore_case_without_mutating_caller_dictionary()
    {
        File.WriteAllText(Path.Combine(_root, "probe.txt"), "selected-project-root");
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectroot"] = _root,
            ["vErSiOn"] = "4.5.6"
        };
        var original = variables.ToArray();
        var command = Shell(OperatingSystem.IsWindows()
            ? "type probe.txt & echo %RELEASE_VARIABLE_PROBE%"
            : "cat probe.txt; printf '%s' \"$RELEASE_VARIABLE_PROBE\"");
        command.Environment["RELEASE_VARIABLE_PROBE"] = "{VERSION}";
        command.ExpectedOutput = "selected-project-root{version}";

        var result = await new ReleaseValidationService().RunCommandAsync(command, variables);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("selected-project-root4.5.6", result.StdOut.Trim());
        Assert.False(result.TimedOut);
        Assert.Equal(original, variables.ToArray());
        Assert.False(variables.ContainsKey("ProjectRoot"));
        Assert.False(variables.ContainsKey("Version"));
    }

    private static ReleaseCommandValidation Shell(string script) => new()
    {
        Name = "Command variable regression probe",
        FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
        Arguments = OperatingSystem.IsWindows() ? ["/d", "/c", script] : ["-c", script],
        TimeoutSeconds = 15
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
