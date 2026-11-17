namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    // Importing approved and required modules (Az, Microsoft.Graph) into analysis runspaces can take minutes.
    private MissingFunctionAnalysisResult? AnalyzeMissingFunctionsWithHeartbeat(
        string? filePath,
        string? code,
        ModulePipelinePlan plan,
        string[] binaryCommands)
    {
        var modules = (plan.ApprovedModules ?? Array.Empty<string>())
            .Concat((plan.RequiredModules ?? Array.Empty<RequiredModuleReference>()).Select(static module => module?.ModuleName ?? string.Empty))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modules.Length > 0)
            _logger.Info($"Resolving missing commands for '{plan.ModuleName}' by importing {string.Join(", ", modules)}.");

        using var heartbeat = OperationHeartbeat.Start(_logger, $"Missing-command analysis for '{plan.ModuleName}'");
        var result = AnalyzeMissingFunctions(filePath, code, plan, binaryCommands);
        if (heartbeat.Elapsed >= OperationHeartbeat.DefaultInterval)
            _logger.Info($"Missing-command analysis for '{plan.ModuleName}' took {DotNetRepositoryReleaseService.FormatDuration(heartbeat.Elapsed)}.");
        return result;
    }

    private void RunTestsAfterMerge(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        TestConfiguration testConfiguration,
        Action<string>? outputLineReceived = null)
    {
        if (plan is null || buildResult is null || testConfiguration is null) return;

        var testsPath = testConfiguration.TestsPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(testsPath))
            throw new InvalidOperationException("TestsAfterMerge is enabled but TestsPath is empty.");

        if (!Path.IsPathRooted(testsPath))
            testsPath = Path.GetFullPath(Path.Combine(plan.ProjectRoot, testsPath));

        var importCfg = plan.ImportModules;
        var importSelf = importCfg?.Self == true;
        var importRequired = importCfg?.RequiredModules == true;
        var importVerbose = importCfg?.Verbose == true;

        var importModules = importRequired
            ? plan.RequiredModules
                .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName))
                .Select(m => new ModuleDependency(
                    name: m.ModuleName.Trim(),
                    requiredVersion: m.RequiredVersion,
                    minimumVersion: m.ModuleVersion,
                    maximumVersion: m.MaximumVersion))
                .ToArray()
            : Array.Empty<ModuleDependency>();

        var spec = new ModuleTestSuiteSpec
        {
            ProjectPath = buildResult.StagingPath,
            TestPath = testsPath,
            Force = testConfiguration.Force,
            SkipDependencies = true,
            SkipImport = !importSelf,
            ImportModules = importModules,
            ImportModulesVerbose = importVerbose
        };

        var result = _hostedOperations is IModulePipelineStreamingHostedOperations streaming
            ? streaming.RunModuleTestSuite(spec, outputLineReceived)
            : _hostedOperations.RunModuleTestSuite(spec);
        if (result.FailedCount > 0)
        {
            var failureMessage = BuildTestsAfterMergeFailureMessage(result);
            if (testConfiguration.Force)
            {
                _logger.Warn($"{failureMessage}{Environment.NewLine}Force was set; continuing.");
            }
            else
            {
                throw new InvalidOperationException(failureMessage);
            }
        }
    }

    private static string BuildTestsAfterMergeFailureMessage(ModuleTestSuiteResult result)
    {
        var message = $"TestsAfterMerge failed ({result.FailedCount} failed).";
        if (result.FailureAnalysis is { FailedTests.Length: > 0 } analysis)
        {
            var filteredFailures = analysis.FailedTests
                .Where(static failure => !string.IsNullOrWhiteSpace(failure.Name) || !string.IsNullOrWhiteSpace(failure.ErrorMessage))
                .ToArray();

            var lines = filteredFailures
                .Take(5)
                .Select(FormatTestsAfterMergeFailureLine)
                .ToArray();

            if (lines.Length > 0)
            {
                var omittedCount = filteredFailures.Length - lines.Length;
                var details = string.Join(Environment.NewLine, lines);
                if (omittedCount > 0)
                    details = $"{details}{Environment.NewLine}Additional failed tests omitted: {omittedCount}.";

                return $"{message}{Environment.NewLine}Failed tests:{Environment.NewLine}{details}";
            }
        }

        var stdErrLine = GetFirstMeaningfulLine(result.StdErr);
        if (!string.IsNullOrWhiteSpace(stdErrLine))
            return $"{message}{Environment.NewLine}stderr: {stdErrLine}";

        var stdOutLine = GetFirstMeaningfulLine(result.StdOut);
        if (!string.IsNullOrWhiteSpace(stdOutLine))
            return $"{message}{Environment.NewLine}stdout: {stdOutLine}";

        return message;
    }

    private static string FormatTestsAfterMergeFailureLine(ModuleTestFailureInfo failure)
    {
        var testName = string.IsNullOrWhiteSpace(failure.Name) ? "<unnamed test>" : failure.Name.Trim();
        var errorLine = GetFirstMeaningfulLine(failure.ErrorMessage);
        return string.IsNullOrWhiteSpace(errorLine)
            ? $"- {testName}"
            : $"- {testName}: {errorLine}";
    }

    private static string? GetFirstMeaningfulLine(string? text)
    {
        if (text is null || text.Length == 0 || string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return null;
    }
}
