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
