namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerHookTests
{
    [Fact]
    public void Plan_AddsCommandHooksAroundPublishAndBundleSteps()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = new DotNetPublishSpec
            {
                Profile = "release",
                Profiles = new[]
                {
                    new DotNetPublishProfile
                    {
                        Name = "release",
                        Default = true,
                        Targets = new[] { "App" }
                    }
                },
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "App",
                        ProjectPath = app,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Styles = new[] { DotNetPublishStyle.PortableCompat }
                        }
                    }
                },
                Bundles = new[]
                {
                    new DotNetPublishBundle
                    {
                        Id = "portable",
                        PrepareFromTarget = "App"
                    }
                },
                Hooks = new[]
                {
                    new DotNetPublishCommandHook
                    {
                        Id = "sync-catalog",
                        Phase = DotNetPublishCommandHookPhase.BeforeTargetPublish,
                        Command = "pwsh",
                        Arguments = new[] { "-NoProfile", "-Command", "exit 0" },
                        GeneratedOutputs = new[] { "Artifacts/{target}/{rid}/catalog.json" },
                        Targets = new[] { "App" }
                    },
                    new DotNetPublishCommandHook
                    {
                        Id = "bundle-summary",
                        Phase = DotNetPublishCommandHookPhase.AfterBundle,
                        Command = "pwsh",
                        Arguments = new[] { "-NoProfile", "-Command", "exit 0" }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var keys = plan.Steps.Select(step => step.Key).ToArray();

            var beforePublish = Array.FindIndex(keys, key => key.StartsWith("hook:BeforeTargetPublish:sync-catalog", StringComparison.Ordinal));
            var publish = Array.FindIndex(keys, key => key.StartsWith("publish:App:", StringComparison.Ordinal));
            var bundle = Array.FindIndex(keys, key => key.StartsWith("bundle:portable:", StringComparison.Ordinal));
            var afterBundle = Array.FindIndex(keys, key => key.StartsWith("hook:AfterBundle:bundle-summary", StringComparison.Ordinal));

            Assert.True(beforePublish >= 0);
            Assert.Equal("Artifacts/{target}/{rid}/catalog.json",
                plan.Steps[beforePublish].HookGeneratedOutputs.Single());
            Assert.True(publish > beforePublish);
            Assert.True(bundle > publish);
            Assert.True(afterBundle > bundle);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_CommandHooksUseContextSpecificKeysAndDefaultTimeout()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var worker = CreateProject(root, "Worker/Worker.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "App",
                        ProjectPath = app,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Styles = new[] { DotNetPublishStyle.PortableCompat }
                        }
                    },
                    new DotNetPublishTarget
                    {
                        Name = "Worker",
                        ProjectPath = worker,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Styles = new[] { DotNetPublishStyle.PortableCompat }
                        }
                    }
                },
                Hooks = new[]
                {
                    new DotNetPublishCommandHook
                    {
                        Id = "catalog",
                        Phase = DotNetPublishCommandHookPhase.BeforeTargetPublish,
                        Command = "dotnet"
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            var hookSteps = plan.Steps
                .Where(step => step.Kind == DotNetPublishStepKind.CommandHook)
                .ToArray();

            Assert.Equal(2, hookSteps.Length);
            Assert.Equal(2, hookSteps.Select(step => step.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(hookSteps, step => Assert.Equal(600, step.HookTimeoutSeconds));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenCommandHookIdsAreDuplicated()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateSpec(root, app);
            spec.Hooks = new[]
            {
                new DotNetPublishCommandHook
                {
                    Id = "catalog",
                    Phase = DotNetPublishCommandHookPhase.BeforeBuild,
                    Command = "dotnet"
                },
                new DotNetPublishCommandHook
                {
                    Id = " catalog ",
                    Phase = DotNetPublishCommandHookPhase.AfterTargetPublish,
                    Command = "dotnet"
                }
            };

            var ex = Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
            Assert.Contains("Duplicate hook ID", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_ThrowsWhenCommandHookCommandIsMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var spec = CreateSpec(root, app);
            spec.Hooks = new[]
            {
                new DotNetPublishCommandHook
                {
                    Id = "catalog",
                    Phase = DotNetPublishCommandHookPhase.BeforeBuild
                }
            };

            var ex = Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
            Assert.Contains("Hooks['catalog'].Command", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RunCommandHook_ExpandsArgumentsWorkingDirectoryAndEnvironment()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = CreateTempRoot();
        try
        {
            var outputPath = Path.Combine(root, "hook-output.txt");
            var step = new DotNetPublishStep
            {
                Key = "hook:BeforeBuild:write",
                Kind = DotNetPublishStepKind.CommandHook,
                HookId = "write",
                HookPhase = DotNetPublishCommandHookPhase.BeforeBuild,
                HookCommand = "pwsh",
                HookArguments = new[]
                {
                    "-NoLogo",
                    "-NoProfile",
                    "-Command",
                    "$value = \"target={0};rid={1};phase=$env:PF_HOOK_PHASE\" -f '{target}', '{rid}'; Set-Content -LiteralPath $env:PF_HOOK_OUTPUT -Value $value"
                },
                HookEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PF_HOOK_OUTPUT"] = outputPath,
                    ["PF_HOOK_PHASE"] = "{phase}"
                },
                HookGeneratedOutputs = new[] { "hook-output.txt" },
                TargetName = "App",
                Runtime = "win-x64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                HookTimeoutSeconds = 30,
                HookRequired = true
            };

            new DotNetPublishPipelineRunner(new NullLogger()).RunCommandHook(
                new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release"
                },
                step);

            Assert.True(File.Exists(outputPath));
            var output = File.ReadAllText(outputPath);
            Assert.Contains("target=App", output, StringComparison.Ordinal);
            Assert.Contains("rid=win-x64", output, StringComparison.Ordinal);
            Assert.Contains("phase=BeforeBuild", output, StringComparison.Ordinal);
            Assert.True(step.HookGeneratedOutputsValidated);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RunCommandHook_RejectsPreexistingDeclaredGeneratedOutput()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = CreateTempRoot();
        try
        {
            string outputPath = Path.Combine(root, "generated", "module.psm1");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "stale");
            var step = new DotNetPublishStep
            {
                Key = "hook:BeforeBundle:module",
                Kind = DotNetPublishStepKind.CommandHook,
                HookId = "module",
                HookPhase = DotNetPublishCommandHookPhase.BeforeBundle,
                HookCommand = "pwsh",
                HookArguments = new[] { "-NoLogo", "-NoProfile", "-Command", "exit 0" },
                HookGeneratedOutputs = new[] { "generated" },
                HookTimeoutSeconds = 30,
                HookRequired = true
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).RunCommandHook(
                    new DotNetPublishPlan { ProjectRoot = root, Configuration = "Release" },
                    step));

            Assert.Contains("must be absent", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RunCommandHook_RequiresDeclaredGeneratedOutputAfterSuccess()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = CreateTempRoot();
        try
        {
            var step = new DotNetPublishStep
            {
                Key = "hook:BeforeBundle:module",
                Kind = DotNetPublishStepKind.CommandHook,
                HookId = "module",
                HookPhase = DotNetPublishCommandHookPhase.BeforeBundle,
                HookCommand = "pwsh",
                HookArguments = new[] { "-NoLogo", "-NoProfile", "-Command", "exit 0" },
                HookGeneratedOutputs = new[] { "generated" },
                HookTimeoutSeconds = 30,
                HookRequired = true
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).RunCommandHook(
                    new DotNetPublishPlan { ProjectRoot = root, Configuration = "Release" },
                    step));

            Assert.Contains("did not produce", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RunCommandHook_FailedOptionalHookRejectsPartialGeneratedOutput()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = CreateTempRoot();
        try
        {
            string outputPath = Path.Combine(root, "generated", "partial.txt");
            var step = new DotNetPublishStep
            {
                Key = "hook:BeforeBundle:optional-module",
                Kind = DotNetPublishStepKind.CommandHook,
                HookId = "optional-module",
                HookPhase = DotNetPublishCommandHookPhase.BeforeBundle,
                HookCommand = "pwsh",
                HookArguments = new[]
                {
                    "-NoLogo",
                    "-NoProfile",
                    "-Command",
                    "New-Item -ItemType Directory -Path (Split-Path -Parent $env:PF_HOOK_OUTPUT) -Force | Out-Null; Set-Content -LiteralPath $env:PF_HOOK_OUTPUT -Value partial; exit 7"
                },
                HookEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PF_HOOK_OUTPUT"] = outputPath
                },
                HookGeneratedOutputs = new[] { "generated" },
                HookTimeoutSeconds = 30,
                HookRequired = false
            };

            var ex = Assert.ThrowsAny<Exception>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).RunCommandHook(
                    new DotNetPublishPlan { ProjectRoot = root, Configuration = "Release" },
                    step));

            Assert.Contains("left a declared generated output", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(step.HookGeneratedOutputsValidated);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void GeneratedOutputTree_RejectsNestedSymbolicLink()
    {
        var root = CreateTempRoot();
        string externalRoot = CreateTempRoot();
        try
        {
            string output = Path.Combine(root, "generated");
            Directory.CreateDirectory(output);
            string externalFile = Path.Combine(externalRoot, "outside.txt");
            File.WriteAllText(externalFile, "outside");
            try
            {
                File.CreateSymbolicLink(Path.Combine(output, "linked.txt"), externalFile);
            }
            catch (Exception linkException) when (linkException is PlatformNotSupportedException or UnauthorizedAccessException)
            {
                return;
            }

            var step = new DotNetPublishStep { HookId = "module" };
            var ex = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.EnsureHookGeneratedOutputTreeIsSafe(root, step, output));

            Assert.Contains("reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(externalRoot);
        }
    }

    [Fact]
    public void DirectoryCopy_RejectsNestedSymbolicLink()
    {
        var root = CreateTempRoot();
        string externalRoot = CreateTempRoot();
        try
        {
            string source = Path.Combine(root, "source");
            string destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(source);
            string externalFile = Path.Combine(externalRoot, "outside.txt");
            File.WriteAllText(externalFile, "outside");
            try
            {
                File.CreateSymbolicLink(Path.Combine(source, "linked.txt"), externalFile);
            }
            catch (Exception linkException) when (linkException is PlatformNotSupportedException or UnauthorizedAccessException)
            {
                return;
            }

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.DirectoryCopy(source, destination));

            Assert.Contains("reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(externalRoot);
        }
    }

    [Fact]
    public void DirectoryCopy_RejectsDestinationSymbolicLink()
    {
        var root = CreateTempRoot();
        string externalRoot = CreateTempRoot();
        try
        {
            string source = Path.Combine(root, "source");
            string destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "payload.txt"), "payload");
            string externalFile = Path.Combine(externalRoot, "outside.txt");
            File.WriteAllText(externalFile, "outside");
            try
            {
                File.CreateSymbolicLink(Path.Combine(destination, "payload.txt"), externalFile);
            }
            catch (Exception linkException) when (linkException is PlatformNotSupportedException or UnauthorizedAccessException)
            {
                return;
            }

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.DirectoryCopy(source, destination));

            Assert.Contains("destination contains a reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("outside", File.ReadAllText(externalFile));
        }
        finally
        {
            TryDelete(root);
            TryDelete(externalRoot);
        }
    }

    [Fact]
    public void RunCommandHook_ReportsTimeoutExplicitly()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = CreateTempRoot();
        try
        {
            var step = new DotNetPublishStep
            {
                Key = "hook:BeforeBuild:slow",
                Kind = DotNetPublishStepKind.CommandHook,
                HookId = "slow",
                HookPhase = DotNetPublishCommandHookPhase.BeforeBuild,
                HookCommand = "pwsh",
                HookArguments = new[] { "-NoLogo", "-NoProfile", "-Command", "[System.Threading.Thread]::Sleep([int]::MaxValue)" },
                HookTimeoutSeconds = 2,
                HookRequired = true
            };

            var ex = Assert.ThrowsAny<Exception>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).RunCommandHook(
                    new DotNetPublishPlan
                    {
                        ProjectRoot = root,
                        Configuration = "Release"
                    },
                    step));

            Assert.Contains("timed out after 2 seconds", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateProject(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return fullPath;
    }

    private static DotNetPublishSpec CreateSpec(string root, string projectPath)
    {
        return new DotNetPublishSpec
        {
            DotNet = new DotNetPublishDotNetOptions
            {
                ProjectRoot = root,
                Restore = false,
                Build = false,
                Runtimes = new[] { "win-x64" }
            },
            Targets = new[]
            {
                new DotNetPublishTarget
                {
                    Name = "App",
                    ProjectPath = projectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        Styles = new[] { DotNetPublishStyle.PortableCompat }
                    }
                }
            }
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                    return true;
            }
        }

        return false;
    }
}
