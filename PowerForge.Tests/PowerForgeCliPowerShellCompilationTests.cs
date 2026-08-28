using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliPowerShellCompilationTests
{
    [Theory]
    [InlineData("powershell build missing.ps1 --kind exe --sing --output json", "powershell.build", "--sing")]
    [InlineData("powershell analyze missing.ps1 --recurs --output json", "powershell.analyze", "--recurs")]
    [InlineData("powershell analyze missing.ps1 --no-recurse --output json", "powershell.analyze", "--no-recurse")]
    [InlineData("powershell analyze one.ps1 --path two.ps1 --output json", "powershell.analyze", "either positionally")]
    [InlineData("powershell build missing.ps1 --kind exe --mode 999 --output json", "powershell.build", "999")]
    [InlineData("powershell build missing.ps1 --resource-mode 999 --output json", "powershell.build", "999")]
    [InlineData("powershell analyze missing.ps1 --resource-mode 999 --output json", "powershell.analyze", "999")]
    [InlineData("powershell explain missing.ps1 --resource-mode 999 --output json", "powershell.explain", "999")]
    public async Task Commands_RejectInvalidOptions(string arguments, string command, string errorFragment)
    {
        var result = await RunCliAsync(FindRepositoryRoot(), arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(command, document.RootElement.GetProperty("command").GetString());
        Assert.Contains(errorFragment, document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library", "Package", "only for Executable")]
    [InlineData("dll", "Package", "only for Executable")]
    public async Task Analyze_RejectsArtifactModesThatCannotProduceTheRequestedKind(
        string kind,
        string mode,
        string errorFragment)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Artifact Mode Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Input.ps1");
        File.WriteAllText(source, "param([int] $Value); return $Value");
        try
        {
            var result = await RunCliAsync(
                FindRepositoryRoot(),
                $"powershell analyze \"{source}\" --kind {kind} --mode {mode} --output json");

            Assert.Equal(1, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(errorFragment, document.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Analyze_AcceptsHybridExecutableMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Hybrid Analyze Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Input.ps1");
        File.WriteAllText(source, "param([int] $Value); return $Value");
        try
        {
            var result = await RunCliAsync(
                FindRepositoryRoot(),
                $"powershell analyze \"{source}\" --kind exe --mode Hybrid --output json");

            Assert.Equal(0, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("Hybrid", 0, "RuntimeFallback")]
    [InlineData("Strict", 1, "Rejected")]
    public async Task Explain_EmitsRelocationSafeCausalDecisionTrace(string mode, int exitCode, string decision)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Explain Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Input.ps1");
        File.WriteAllText(source, "Invoke-DynamicThing $Name");
        try
        {
            var result = await RunCliAsync(
                FindRepositoryRoot(),
                $"powershell explain \"{source}\" --kind exe --mode {mode} --output json");

            Assert.Equal(exitCode, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.Equal("powershell.explain", document.RootElement.GetProperty("command").GetString());
            var explanation = document.RootElement.GetProperty("result");
            Assert.Equal(1, explanation.GetProperty("schemaVersion").GetInt32());
            var file = Assert.Single(explanation.GetProperty("files").EnumerateArray());
            Assert.Equal("Input.ps1", file.GetProperty("relativePath").GetString());
            var unit = Assert.Single(file.GetProperty("units").EnumerateArray());
            Assert.Equal(decision, unit.GetProperty("decision").GetString());
            Assert.Equal(24, unit.GetProperty("unitId").GetString()!.Length);
            Assert.NotEmpty(unit.GetProperty("causes").EnumerateArray());
            Assert.DoesNotContain(root, explanation.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Explain_HybridModuleReportsDuplicateFunctionsAfterFinalCollisionRouting()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Final Explain Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Input.psm1");
        File.WriteAllText(source, "function Get-Duplicate { return 1 }\nfunction Get-Duplicate { return 2 }");
        try
        {
            var result = await RunCliAsync(
                FindRepositoryRoot(),
                $"powershell explain \"{source}\" --kind dll --mode Hybrid --framework net10.0 --output json");

            Assert.Equal(0, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
            using var document = JsonDocument.Parse(result.StdOut);
            var explanation = document.RootElement.GetProperty("result");
            Assert.Equal(0, explanation.GetProperty("typedUnits").GetInt32());
            Assert.Equal(2, explanation.GetProperty("runtimeFallbackUnits").GetInt32());
            var units = Assert.Single(explanation.GetProperty("files").EnumerateArray())
                .GetProperty("units").EnumerateArray().ToArray();
            Assert.All(units, static unit => Assert.Equal("RuntimeFallback", unit.GetProperty("decision").GetString()));
            Assert.All(units, static unit => Assert.NotEmpty(unit.GetProperty("causes").EnumerateArray()));
            Assert.Contains(units.SelectMany(static unit => unit.GetProperty("causes").EnumerateArray()), static cause =>
                cause.GetProperty("message").GetString()!.Contains("multiple retained definitions", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BuildStrictTypedExecutable_ExposesRuntimeIndependentCliContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Typed Executable Tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "compiled output");
        Directory.CreateDirectory(output);
        var source = Path.Combine(root, "Typed Script.ps1");
        File.WriteAllText(
            source,
            """
            param([int] $Count)
            [long] $total = 0
            for ([int] $value = 1; $value -le $Count; $value++) { $total += $value }
            return $total
            """);

        try
        {
            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build \"{source}\" --kind exe --mode Strict --framework net10.0 --allow-unreviewed-dependencies --out \"{output}\" --name TypedCliProof --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("typed executable build", build));
            string artifactPath;
            using (var document = JsonDocument.Parse(build.StdOut))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                var manifest = document.RootElement.GetProperty("result").GetProperty("manifest");
                Assert.Equal(1, manifest.GetProperty("compiledMethods").GetInt32());
                Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
                Assert.False(manifest.GetProperty("usesPowerShellRuntimeFallback").GetBoolean());
                artifactPath = manifest.GetProperty("artifactPath").GetString()!;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = artifactPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("--Count=100");
            process.Start();
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, standardError);
            Assert.Equal("5050", standardOutput.Trim());
            Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BuildStrictMultiFileExecutable_UsesExplicitEntrypointAndDirectLocalCalls()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Multi File Typed Tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "compiled output");
        Directory.CreateDirectory(output);
        var entryPoint = Path.Combine(root, "Tool.ps1");
        var helper = Path.Combine(root, "Helper.ps1");
        File.WriteAllText(entryPoint, "param([int] $Value); . \"$PSScriptRoot/Helper.ps1\"; return Get-Value -Value $Value");
        File.WriteAllText(helper, "function Get-Value { param([int] $Value) return $Value }");

        try
        {
            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build \"{entryPoint}\" --path \"{helper}\" --entry-point \"{entryPoint}\" --kind exe --mode Strict --framework net10.0 --allow-unreviewed-dependencies --out \"{output}\" --name TypedMultiCliProof --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("multi-file typed executable build", build));
            string artifactPath;
            using (var document = JsonDocument.Parse(build.StdOut))
            {
                var manifest = document.RootElement.GetProperty("result").GetProperty("manifest");
                Assert.Equal(2, manifest.GetProperty("compiledMethods").GetInt32());
                Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
                artifactPath = manifest.GetProperty("artifactPath").GetString()!;
            }
            using var process = Process.Start(new ProcessStartInfo(artifactPath, "--Value=73")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal((0, "73", string.Empty), (process.ExitCode, standardOutput.Trim(), standardError.Trim()));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AnalyzeAndBuildTypedLibrary_ExposeStableJsonCliContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Compilation Tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "compiled output");
        Directory.CreateDirectory(output);
        var resources = Path.Combine(root, "Resources");
        Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "runtime.js"), "console.log('runtime');");
        var source = Path.Combine(root, "Typed Functions.psm1");
        File.WriteAllText(
            source,
            """
            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }
            """);

        try
        {
            var analyze = await RunCliAsync(repositoryRoot, $"powershell analyze \"{source}\" --kind library --mode Strict --output json");
            Assert.True(analyze.ExitCode == 0, FormatFailure("analyze", analyze));
            using (var document = JsonDocument.Parse(analyze.StdOut))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("powershell.analyze", document.RootElement.GetProperty("command").GetString());
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(1, result.GetProperty("totalUnits").GetInt32());
                Assert.Equal(1, result.GetProperty("compilableUnits").GetInt32());
                Assert.Contains(
                    result.GetProperty("dependencies").EnumerateArray(),
                    dependency => dependency.GetProperty("relativePath").GetString() == "Resources/runtime.js" &&
                                  dependency.GetProperty("disposition").GetString() == "NotIncluded" &&
                                  dependency.GetProperty("selection").GetString() == "Unclassified");
                Assert.Equal(1, result.GetProperty("resourceSummary").GetProperty("unclassifiedFiles").GetInt32());
            }

            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build \"{source}\" --kind library --mode Strict --framework net10.0 --allow-unreviewed-dependencies --include-resource \"Resources/**\" --out \"{output}\" --name CliProof --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("build", build));
            using (var document = JsonDocument.Parse(build.StdOut))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("powershell.build", document.RootElement.GetProperty("command").GetString());
                var manifest = document.RootElement.GetProperty("result").GetProperty("manifest");
                Assert.Equal(1, manifest.GetProperty("compiledMethods").GetInt32());
                Assert.Equal(0, manifest.GetProperty("runtimeFallbackUnits").GetInt32());
                Assert.Equal(0, manifest.GetProperty("omittedUnits").GetInt32());
                Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
                Assert.True(File.Exists(manifest.GetProperty("artifactPath").GetString()));
                Assert.True(File.Exists(Path.Combine(output, "Resources", "runtime.js")));
                Assert.Contains(
                    manifest.GetProperty("dependencies").EnumerateArray(),
                    dependency => dependency.GetProperty("relativePath").GetString() == "Resources/runtime.js" &&
                                  dependency.GetProperty("disposition").GetString() == "CopiedAdjacent" &&
                                  dependency.GetProperty("selection").GetString() == "ExplicitInclude");
                Assert.Equal(1, manifest.GetProperty("resourceSummary").GetProperty("includedFiles").GetInt32());
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AnalyzeDependencyGraph_RoundTripsAsReviewedBuildLock()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Dependency Lock Tests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var output = Path.Combine(root, "output");
        var source = Path.Combine(sourceRoot, "Locked.psm1");
        var lockPath = Path.Combine(root, "dependency-lock.json");
        Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(source, "function Get-LockedValue { return 42 }");
        try
        {
            var analyze = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --kind library --mode Strict --framework net10.0 --out \"{output}\" --output json");
            Assert.True(analyze.ExitCode == 0, FormatFailure("dependency-lock analyze", analyze));
            using (var document = JsonDocument.Parse(analyze.StdOut))
            {
                var graph = document.RootElement.GetProperty("result").GetProperty("dependencyGraph");
                Assert.Contains("\"roles\":\"", graph.GetRawText(), StringComparison.Ordinal);
                File.WriteAllText(lockPath, graph.GetRawText());
            }

            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build \"{source}\" --kind library --mode Strict --framework net10.0 --out \"{output}\" --dependency-lock \"{lockPath}\" --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("reviewed dependency-lock build", build));
            using var buildDocument = JsonDocument.Parse(build.StdOut);
            var manifest = buildDocument.RootElement.GetProperty("result").GetProperty("manifest");
            Assert.True(manifest.GetProperty("dependencyLockReviewed").GetBoolean());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AnalyzeStrictExecutableUsesBuildCapabilityPolicyForLocalCalls()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Analyze Capability Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Tool.ps1");
        File.WriteAllText(source, "function Get-Inner { param([int] $Value) return $Value }; function Get-Outer { return Get-Inner -Value 7 }; return Get-Outer");
        try
        {
            var analyze = await RunCliAsync(
                FindRepositoryRoot(),
                $"powershell analyze \"{source}\" --kind exe --mode Strict --framework net10.0 --output json");

            Assert.True(analyze.ExitCode == 0, FormatFailure("strict executable analyze", analyze));
            using var document = JsonDocument.Parse(analyze.StdOut);
            var result = document.RootElement.GetProperty("result");
            Assert.Equal(3, result.GetProperty("compilableUnits").GetInt32());
            Assert.Equal(0, result.GetProperty("runtimeFallbackUnits").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CommandsAcceptPositionalSourceAfterOptions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Positional Ordering Tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Tool.ps1");
        File.WriteAllText(source, "return 1");
        try
        {
            var defaultAnalyze = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --output json");
            Assert.True(defaultAnalyze.ExitCode == 0, FormatFailure("default analyze", defaultAnalyze));

            var analyze = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze --mode Strict \"{source}\" --framework net10.0 --output json");
            Assert.True(analyze.ExitCode == 0, FormatFailure("option-first analyze", analyze));

            var census = await RunCliAsync(
                repositoryRoot,
                $"powershell census --framework net10.0 \"{source}\" --output json");
            Assert.True(census.ExitCode == 0, FormatFailure("option-first census", census));

            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build --mode Strict --kind exe \"{source}\" --framework net10.0 --allow-unreviewed-dependencies --out \"{output}\" --name PositionalProof --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("option-first build", build));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Census_WritesAndEnforcesRepeatableCoverageBaseline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Census Tests", Guid.NewGuid().ToString("N"));
        var product = Path.Combine(root, "SampleProduct");
        var baseline = Path.Combine(root, "census.json");
        Directory.CreateDirectory(product);
        var source = Path.Combine(product, "Functions.psm1");
        File.WriteAllText(source, "function Add-TypedValue { param([int] $Number) [int] $result = $Number; $result += 1; return $result }");

        try
        {
            var capture = await RunCliAsync(
                repositoryRoot,
                $"powershell census \"{product}\" --framework net10.0 --write-baseline \"{baseline}\" --output json");
            Assert.True(capture.ExitCode == 0, FormatFailure("census capture", capture));
            Assert.True(File.Exists(baseline));
            using (var document = JsonDocument.Parse(capture.StdOut))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(1, result.GetProperty("sourceFiles").GetInt32());
                Assert.True(result.GetProperty("compilableUnits").GetInt32() == 1, capture.StdOut);
                Assert.True(result.GetProperty("passed").GetBoolean());
            }

            File.WriteAllText(source, "function Add-TypedValue { throw 'regression' }");
            var regression = await RunCliAsync(
                repositoryRoot,
                $"powershell census \"{product}\" --framework net10.0 --baseline \"{baseline}\" --output json");
            Assert.Equal(1, regression.ExitCode);
            using var regressionDocument = JsonDocument.Parse(regression.StdOut);
            var regressionResult = regressionDocument.RootElement.GetProperty("result");
            Assert.False(regressionResult.GetProperty("passed").GetBoolean());
            Assert.Contains(
                regressionResult.GetProperty("regressions").EnumerateArray(),
                item => item.GetProperty("metric").GetString() == "CompilableUnits");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Analyze_HonorsRequestedTargetFrameworkMemberSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Target Analysis Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Target Surface.ps1");
        File.WriteAllText(source, "function New-Version7Guid { return [System.Guid]::CreateVersion7() }");

        try
        {
            var net8 = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --mode Strict --framework net8.0 --output json");
            Assert.Equal(1, net8.ExitCode);
            using (var document = JsonDocument.Parse(net8.StdOut))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal("net8.0", result.GetProperty("targetFramework").GetString());
                Assert.Equal(0, result.GetProperty("compilableUnits").GetInt32());
            }

            var defaultTarget = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --mode Strict --output json");
            Assert.Equal(1, defaultTarget.ExitCode);
            using (var document = JsonDocument.Parse(defaultTarget.StdOut))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal("net8.0", result.GetProperty("targetFramework").GetString());
                Assert.Equal(0, result.GetProperty("compilableUnits").GetInt32());
            }

            var net10 = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --mode Strict --framework net10.0 --output json");
            Assert.Equal(0, net10.ExitCode);
            using var net10Document = JsonDocument.Parse(net10.StdOut);
            var net10Result = net10Document.RootElement.GetProperty("result");
            Assert.Equal("net10.0", net10Result.GetProperty("targetFramework").GetString());
            Assert.Equal(1, net10Result.GetProperty("compilableUnits").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AnalyzeManifest_UsesTheResolvedBuildSourceGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Resolved Analysis Tests", Guid.NewGuid().ToString("N"));
        var publicDirectory = Path.Combine(root, "Public");
        Directory.CreateDirectory(publicDirectory);
        var manifest = Path.Combine(root, "Sample.psd1");
        var module = Path.Combine(root, "Sample.psm1");
        var included = Path.Combine(publicDirectory, "Get-Included.ps1");
        var unrelated = Path.Combine(root, "Unrelated.ps1");
        File.WriteAllText(manifest, "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-Included') }");
        File.WriteAllText(module, ". \"$PSScriptRoot/Public/Get-Included.ps1\"; Export-ModuleMember -Function Get-Included");
        File.WriteAllText(included, "function Get-Included { return 7 }");
        File.WriteAllText(unrelated, "function Invoke-Unrelated { Invoke-DynamicCommand }");

        try
        {
            var analyze = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{manifest}\" --mode Hybrid --output json");
            Assert.Equal(0, analyze.ExitCode);
            using var document = JsonDocument.Parse(analyze.StdOut);
            var files = document.RootElement.GetProperty("result").GetProperty("files").EnumerateArray().ToArray();
            Assert.Equal(2, files.Length);
            Assert.Contains(files, file => Path.GetFullPath(file.GetProperty("fullPath").GetString()!) == Path.GetFullPath(module));
            Assert.Contains(files, file => Path.GetFullPath(file.GetProperty("fullPath").GetString()!) == Path.GetFullPath(included));
            Assert.DoesNotContain(files, file => Path.GetFullPath(file.GetProperty("fullPath").GetString()!) == Path.GetFullPath(unrelated));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(string repositoryRoot, string arguments)
    {
        var cli = Path.Combine(repositoryRoot, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{cli}\" {arguments}",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(120))) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("PowerShell compilation CLI test timed out.");
        }
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        string workingDirectory,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge.Cli", "PowerForge.Cli.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the repository root for PowerForge CLI tests.");
    }

    private static string FormatFailure(string operation, (int ExitCode, string StdOut, string StdErr) result)
        => $"CLI {operation} exit code {result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}";
}
