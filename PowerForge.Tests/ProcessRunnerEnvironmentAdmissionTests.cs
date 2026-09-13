namespace PowerForge.Tests;

public sealed class ProcessRunnerEnvironmentAdmissionTests {
    private const string ValueVariable = "POWERFORGE_ADMISSION_VALUE";
    private const string EmptyVariable = "POWERFORGE_ADMISSION_EMPTY";
    private const string SensitiveValue = "private-environment-value-7aeb";

    public static IEnumerable<object?[]> InvalidOverrides() {
        foreach (var ownProcessTree in new[] { false, true }) {
            foreach (var key in new[] { "", "INVALID=KEY", "INVALID\0KEY" }) {
                yield return new object?[] { ownProcessTree, key, SensitiveValue };
                yield return new object?[] { ownProcessTree, key, null };
            }

            yield return new object?[] { ownProcessTree, ValueVariable, SensitiveValue + "\0suffix" };
        }
    }

    [Theory]
    [InlineData(false, "FileName")]
    [InlineData(true, "FileName")]
    [InlineData(false, "WorkingDirectory")]
    [InlineData(true, "WorkingDirectory")]
    [InlineData(false, "Arguments")]
    [InlineData(true, "Arguments")]
    public async Task RunAsync_rejects_NUL_in_launch_fields_before_start(bool ownProcessTree, string field) {
        var probe = CreateProbe(new Dictionary<string, string?>());
        var request = new ProcessRunRequest(
            field == "FileName" ? probe.FileName + "\0ignored" : probe.FileName,
            field == "WorkingDirectory" ? probe.WorkingDirectory + "\0ignored" : probe.WorkingDirectory,
            field == "Arguments" ? new[] { "before\0after" } : probe.Arguments,
            TimeSpan.FromSeconds(30));
        var reachedPreStart = false;
        var startedProcess = false;
        request.SetPreStartBoundary(() => reachedPreStart = true);
        request.SetStartedProcessBoundary(_ => startedProcess = true);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new ProcessRunner(ownProcessTree).RunAsync(request));

        Assert.False(reachedPreStart);
        Assert.False(startedProcess);
    }

    [Theory]
    [MemberData(nameof(InvalidOverrides))]
    public async Task RunAsync_rejects_invalid_overrides_before_start_without_disclosing_values(
        bool ownProcessTree, string key, string? value) {
        var request = CreateProbe(new Dictionary<string, string?> { [key] = value });
        var reachedPreStart = false;
        var startedProcess = false;
        request.SetPreStartBoundary(() => reachedPreStart = true);
        request.SetStartedProcessBoundary(_ => startedProcess = true);

        var error = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new ProcessRunner(ownProcessTree).RunAsync(request));

        Assert.False(reachedPreStart);
        Assert.False(startedProcess);
        Assert.DoesNotContain(SensitiveValue, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_preserves_valid_values_empty_entries_and_explicit_removal(bool ownProcessTree) {
        const string value = "first=second\nZażółć gęślą jaźń 日本語";
        var removedVariable = OperatingSystem.IsWindows() ? "TEMP" : "HOME";
        Assert.NotNull(Environment.GetEnvironmentVariable(removedVariable));
        var request = CreateProbe(new Dictionary<string, string?> {
            [ValueVariable] = value,
            [EmptyVariable] = string.Empty,
            [removedVariable] = null
        });

        var result = await new ProcessRunner(ownProcessTree).RunAsync(request);

        Assert.True(result.Succeeded, result.StdErr);
        var output = "\n" + result.StdOut.Replace("\r\n", "\n");
        Assert.Contains("\n" + ValueVariable + "=" + value + "\n", output, StringComparison.Ordinal);
        Assert.Contains("\n" + EmptyVariable + "=\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\n" + removedVariable + "=", output, StringComparison.Ordinal);
    }

    private static ProcessRunRequest CreateProbe(IReadOnlyDictionary<string, string?> overrides) {
        if (OperatingSystem.IsWindows()) {
            // Read the native environment dictionary: PowerShell 5.1's Env: provider
            // does not reliably distinguish an empty entry from an absent entry.
            const string script = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding; " +
                "$values = [Environment]::GetEnvironmentVariables(); " +
                "foreach ($name in @('POWERFORGE_ADMISSION_VALUE', 'POWERFORGE_ADMISSION_EMPTY', 'TEMP')) { " +
                "if ($values.Contains($name)) { [Console]::WriteLine($name + '=' + $values[$name]) } }";
            return new ProcessRunRequest(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Path.GetTempPath(),
                new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script },
                TimeSpan.FromSeconds(30),
                overrides);
        }

        // Use only shell builtins and print only the requested variables. HOME
        // is removed because a shell may initialize PATH when it is absent.
        const string unixScript =
            "if [ \"${POWERFORGE_ADMISSION_VALUE+x}\" = x ]; then " +
            "printf 'POWERFORGE_ADMISSION_VALUE=%s\\n' \"$POWERFORGE_ADMISSION_VALUE\"; fi; " +
            "if [ \"${POWERFORGE_ADMISSION_EMPTY+x}\" = x ]; then " +
            "printf 'POWERFORGE_ADMISSION_EMPTY=%s\\n' \"$POWERFORGE_ADMISSION_EMPTY\"; fi; " +
            "if [ \"${HOME+x}\" = x ]; then printf 'HOME=present\\n'; fi";
        return new ProcessRunRequest(
            "/bin/sh",
            Path.GetTempPath(),
            new[] { "-c", unixScript },
            TimeSpan.FromSeconds(30),
            overrides);
    }
}
