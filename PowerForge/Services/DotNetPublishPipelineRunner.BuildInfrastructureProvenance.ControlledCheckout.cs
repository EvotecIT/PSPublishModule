namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static bool TryCreateControlledBuildEnvironment(
        IReadOnlyDictionary<string, string?> environmentVariables,
        string gitRoot,
        string controlledSourceRoot,
        out IReadOnlyDictionary<string, string?> controlledEnvironment)
        => TryCreateControlledBuildEnvironment(
            environmentVariables,
            Array.Empty<string>(),
            gitRoot,
            controlledSourceRoot,
            gitRoot,
            out controlledEnvironment);

    internal static bool TryCreateControlledBuildEnvironment(
        IReadOnlyDictionary<string, string?> environmentVariables,
        IReadOnlyCollection<string> controlledEnvironmentVariableNames,
        string gitRoot,
        string controlledSourceRoot,
        out IReadOnlyDictionary<string, string?> controlledEnvironment)
        => TryCreateControlledBuildEnvironment(
            environmentVariables,
            controlledEnvironmentVariableNames,
            gitRoot,
            controlledSourceRoot,
            gitRoot,
            out controlledEnvironment);

    private static bool TryCreateControlledBuildEnvironment(
        IReadOnlyDictionary<string, string?> environmentVariables,
        IReadOnlyCollection<string> controlledEnvironmentVariableNames,
        string gitRoot,
        string controlledSourceRoot,
        string buildInputBaseDirectory,
        out IReadOnlyDictionary<string, string?> controlledEnvironment)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var inheritedVariables = Environment.GetEnvironmentVariables();
        foreach (object? key in inheritedVariables.Keys)
        {
            string? name = key?.ToString();
            if (!string.IsNullOrWhiteSpace(name) && !IsApprovedControlledBuildEnvironmentVariable(name!))
                values[name!] = null;
        }
        string environmentRoot = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(controlledSourceRoot))!,
            "environment");
        string configurationRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "config")).FullName;
        string cacheRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "cache")).FullName;
        string homeRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "home")).FullName;
        string temporaryRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "temp")).FullName;
        string packageRoot = Directory.CreateDirectory(Path.Combine(environmentRoot, "packages")).FullName;
        values["APPDATA"] = configurationRoot;
        values["LOCALAPPDATA"] = cacheRoot;
        values["XDG_CONFIG_HOME"] = configurationRoot;
        values["XDG_CACHE_HOME"] = cacheRoot;
        values["HOME"] = homeRoot;
        values["USERPROFILE"] = homeRoot;
        values["TEMP"] = temporaryRoot;
        values["TMP"] = temporaryRoot;
        values["TMPDIR"] = temporaryRoot;
        values["NUGET_PACKAGES"] = packageRoot;
        var controlledNames = new HashSet<string>(
            controlledEnvironmentVariableNames,
            StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> variable in environmentVariables)
        {
            if (IsUncontrolledRuntimeInjectionEnvironmentVariable(variable.Key))
            {
                controlledEnvironment = values;
                return false;
            }
            if (!controlledNames.Contains(variable.Key) || variable.Value is null)
                continue;
            if (!TryRemapControlledBuildValue(
                    variable.Value,
                    gitRoot,
                    controlledSourceRoot,
                    buildInputBaseDirectory,
                    out string controlledValue))
            {
                controlledEnvironment = values;
                return false;
            }
            values[variable.Key] = controlledValue;
        }
        if (!TryResolveTrustedBuildTool("dotnet", out string dotNetPath))
        {
            controlledEnvironment = values;
            return false;
        }
        string dotNetRoot = Path.GetDirectoryName(dotNetPath)!;
        values["PATH"] = dotNetRoot;
        values["DOTNET_ROOT"] = dotNetRoot;
        values["DOTNET_ROOT(x86)"] = null;
        values["HTTP_PROXY"] = "http://127.0.0.1:1";
        values["HTTPS_PROXY"] = "http://127.0.0.1:1";
        values["ALL_PROXY"] = "http://127.0.0.1:1";
        values["NO_PROXY"] = string.Empty;
        controlledEnvironment = values;
        return true;
    }

    private static bool IsApprovedControlledBuildEnvironmentVariable(string name)
        => name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("WINDIR", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ComSpec", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("PROCESSOR_ARCHITECTURE", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("PROCESSOR_ARCHITEW6432", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgramFiles", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgramFiles(x86)", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ProgramW6432", StringComparison.OrdinalIgnoreCase);

    private static bool IsUncontrolledRuntimeInjectionEnvironmentVariable(string name)
    {
        string[] exactNames =
        {
            "DOTNET_STARTUP_HOOKS",
            "DOTNET_ADDITIONAL_DEPS",
            "DOTNET_SHARED_STORE",
            "DOTNET_DiagnosticPorts",
            "DOTNET_GCName",
            "DOTNET_GCPath",
            "DOTNET_HOST_PATH",
            "DOTNET_JitName",
            "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
            "MSBUILDENABLEALLPROPERTYFUNCTIONS",
            "MSBUILDADDITIONALSDKRESOLVERSFOLDER",
            "MSBUILD_EXE_PATH",
            "MSBUILDLEGACYEXTENSIONSPATH",
            "MSBUILDUSEREXTENSIONSPATH",
            "MSBUILDSDKSPATH",
            "MSBUILDEXTENSIONSPATH",
            "MSBUILDEXTENSIONSPATH32",
            "MSBUILDEXTENSIONSPATH64",
            "ROSLYNTARGETSPATH",
            "CSCTOOLPATH",
            "CSCTOOLEXE",
            "VBCTOOLPATH",
            "VBCTOOLEXE",
            "FSCTOOLPATH",
            "FSCTOOLEXE",
            "NUGET_PLUGIN_PATHS",
            "NUGET_CREDENTIALPROVIDERS_PATH"
        };
        return IsNativeLoaderInjectionEnvironmentVariable(name) ||
               exactNames.Any(value => name.Equals(value, StringComparison.OrdinalIgnoreCase)) ||
               name.StartsWith("CustomBeforeMicrosoft", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("CustomAfterMicrosoft", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("DOTNET_ROOT_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("ImportUserLocationsByWildcard", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("MSBUILDNODEHANDLER", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("COMPlus_GCName", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("COMPlus_GCPath", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("COMPlus_JitName", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("CORECLR_PROFILER", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("CORECLR_ENABLE_PROFILING", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("COR_PROFILER", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("COR_ENABLE_PROFILING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRemapControlledBuildValue(
        string value,
        string gitRoot,
        string controlledSourceRoot,
        string buildInputBaseDirectory,
        out string remappedValue)
    {
        string[] segments = value.Split(';');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            int start = 0;
            while (start < segment.Length && char.IsWhiteSpace(segment[start]))
                start++;
            int end = segment.Length;
            while (end > start && char.IsWhiteSpace(segment[end - 1]))
                end--;
            char quote = '\0';
            if (end - start >= 2 &&
                (segment[start] == '\'' || segment[start] == '"') &&
                segment[end - 1] == segment[start])
            {
                quote = segment[start];
                start++;
                end--;
            }

            string candidate = segment.Substring(start, end - start);
            if (Path.IsPathRooted(candidate))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate);
                }
                catch
                {
                    remappedValue = string.Empty;
                    return false;
                }
                if (!IsSameOrBelowBuildInputPath(fullPath, gitRoot))
                {
                    remappedValue = string.Empty;
                    return false;
                }
                string relativePath = FrameworkCompatibility.GetRelativePath(gitRoot, fullPath);
                string controlledPath = Path.GetFullPath(Path.Combine(controlledSourceRoot, relativePath));
                if (!IsSameOrBelowBuildInputPath(controlledPath, controlledSourceRoot))
                {
                    remappedValue = string.Empty;
                    return false;
                }

                string prefix = segment.Substring(0, quote == '\0' ? start : start - 1);
                string suffix = segment.Substring(quote == '\0' ? end : end + 1);
                segments[index] = prefix +
                    (quote == '\0' ? string.Empty : quote.ToString()) +
                    controlledPath +
                    (quote == '\0' ? string.Empty : quote.ToString()) +
                    suffix;
                continue;
            }

            if (ContainsRootedBuildValue(candidate, gitRoot) ||
                ContainsEscapingRelativeBuildValue(
                    candidate,
                    buildInputBaseDirectory,
                    gitRoot))
            {
                remappedValue = string.Empty;
                return false;
            }
        }

        remappedValue = string.Join(";", segments);
        return true;
    }

    internal static bool ContainsRootedBuildValue(string value, string? gitRoot)
    {
        value = DecodeMsBuildEscapes(value);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.IsNullOrWhiteSpace(gitRoot) &&
            value.IndexOf(Path.GetFullPath(gitRoot!), comparison) >= 0)
            return true;

        for (int index = 0; index < value.Length; index++)
        {
            if (index > 0 &&
                !char.IsWhiteSpace(value[index - 1]) &&
                "=,;|([{'\"".IndexOf(value[index - 1]) < 0)
            {
                continue;
            }
            int candidateStart = index;
            char quote = '\0';
            if (value[index] == '\'' || value[index] == '"')
            {
                quote = value[index];
                candidateStart++;
            }
            else if (index > 0 && (value[index - 1] == '\'' || value[index - 1] == '"'))
            {
                quote = value[index - 1];
            }

            string candidate = value.Substring(candidateStart);
            if (quote != '\0')
            {
                int closingQuote = candidate.IndexOf(quote);
                if (closingQuote >= 0)
                    candidate = candidate.Substring(0, closingQuote);
            }
            if (candidate.Length == 1 &&
                (candidate[0] == Path.DirectorySeparatorChar ||
                 candidate[0] == Path.AltDirectorySeparatorChar))
            {
                continue;
            }
            if (Path.IsPathRooted(candidate))
                return true;
        }
        return false;
    }

    internal static bool ContainsEscapingRelativeBuildValue(
        string value,
        string baseDirectory,
        string allowedRoot)
    {
        value = DecodeMsBuildEscapes(value);
        string normalized = value.Replace('\\', '/');
        for (int index = 0; index < normalized.Length; index++)
        {
            if (index > 0 &&
                !char.IsWhiteSpace(normalized[index - 1]) &&
                "=,;|([{'\"".IndexOf(normalized[index - 1]) < 0)
            {
                continue;
            }
            string candidate = normalized.Substring(index).TrimStart('\'', '"');
            int end = candidate.IndexOfAny(new[] { ';', ',', '|', ')', ']', '}', '\'', '"', ' ', '\t', '\r', '\n' });
            if (end >= 0)
                candidate = candidate.Substring(0, end);
            string[] segments = candidate.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (!segments.Any(segment => segment == ".."))
                continue;
            if (candidate.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf("%(", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            try
            {
                string resolved = Path.GetFullPath(Path.Combine(baseDirectory, candidate));
                if (!IsSameOrBelowBuildInputPath(resolved, allowedRoot))
                    return true;
            }
            catch
            {
                return true;
            }
        }
        return false;
    }

    internal static bool ContainsUncontrolledEnvironmentReference(string value)
    {
        value = DecodeMsBuildEscapes(value);
        if (value.IndexOf("System.Environment", StringComparison.OrdinalIgnoreCase) >= 0 &&
            value.IndexOf("GetEnvironmentVariable", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        string[] names =
        {
            "SystemRoot",
            "WINDIR",
            "ComSpec",
            "PATHEXT",
            "PROCESSOR_ARCHITECTURE",
            "PROCESSOR_ARCHITEW6432",
            "ProgramFiles",
            "ProgramFiles(x86)",
            "ProgramW6432",
            "CommonProgramFiles",
            "CommonProgramFiles(x86)",
            "CommonProgramW6432",
            "SystemDrive"
        };
        return names.Any(name =>
            value.IndexOf("$(" + name + ")", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("%" + name + "%", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static bool ContainsUncontrolledFileSystemPropertyFunction(string value)
    {
        value = DecodeMsBuildEscapes(value);
        string[] prefixes =
        {
            "$([System.IO.Path]::",
            "$([System.IO.File]::",
            "$([System.IO.Directory]::",
            "$([MSBuild]::NormalizePath(",
            "$([MSBuild]::NormalizeDirectory(",
            "$([MSBuild]::GetDirectoryNameOfFileAbove(",
            "$([MSBuild]::GetPathOfFileAbove("
        };
        return prefixes.Any(prefix =>
            value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static bool ContainsUncontrolledAmbientPropertyFunction(string value)
    {
        value = DecodeMsBuildEscapes(value);
        string[] prefixes =
        {
            "$([System.Environment]::",
            "$([System.DateTime]::",
            "$([System.DateTimeOffset]::",
            "$([System.Guid]::",
            "$([System.Random]::",
            "$([System.Security.Cryptography.RandomNumberGenerator]::",
            "$([System.Globalization.CultureInfo]::",
            "$([System.TimeZoneInfo]::",
            "$([System.Diagnostics.Process]::",
            "$([System.Reflection.Assembly]::",
            "$([System.Runtime.InteropServices.RuntimeInformation]::",
            "$([System.OperatingSystem]::",
            "$([System.AppContext]::",
            "$([Microsoft.Win32.Registry]::",
            "$([MSBuild]::GetRegistryValue(",
            "$([MSBuild]::GetRegistryValueFromView(",
            "$([MSBuild]::DoesTaskHostExist(",
            "$([MSBuild]::GetCurrentToolsDirectory(",
            "$([MSBuild]::GetToolsDirectory",
            "$([MSBuild]::GetVsInstallRoot(",
            "$([MSBuild]::GetProgramFiles32(",
            "$([MSBuild]::IsOSPlatform(",
            "$([MSBuild]::IsOSUnixLike("
        };
        return prefixes.Any(prefix =>
            value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool ContainsValueProducingPropertyFunction(string value)
    {
        value = DecodeMsBuildEscapes(value);
        return value.IndexOf("$([", StringComparison.Ordinal) >= 0;
    }

    private static bool ContainsExecutableResponseFileSwitch(string value)
    {
        string candidate = DecodeMsBuildEscapes(value).Trim().Trim('"', '\'').Trim();
        if (candidate.StartsWith("@", StringComparison.Ordinal))
            return true;
        if (!candidate.StartsWith("-", StringComparison.Ordinal) &&
            !candidate.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        candidate = candidate.Substring(1);
        return candidate.StartsWith("logger:", StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith("l:", StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith("distributedlogger:", StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith("dl:", StringComparison.OrdinalIgnoreCase);
    }

    internal static string DecodeMsBuildEscapes(string value)
    {
        if (value.IndexOf('%') < 0)
            return value;
        var decoded = new System.Text.StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && index + 2 < value.Length &&
                TryReadHex(value[index + 1], out int high) &&
                TryReadHex(value[index + 2], out int low))
            {
                decoded.Append((char)((high << 4) | low));
                index += 2;
            }
            else
            {
                decoded.Append(value[index]);
            }
        }
        return decoded.ToString();

        bool TryReadHex(char character, out int result)
        {
            if (character >= '0' && character <= '9')
            {
                result = character - '0';
                return true;
            }
            if (character >= 'a' && character <= 'f')
            {
                result = character - 'a' + 10;
                return true;
            }
            if (character >= 'A' && character <= 'F')
            {
                result = character - 'A' + 10;
                return true;
            }
            result = 0;
            return false;
        }
    }

    private static bool TryCreateControlledSourceCheckout(
        string projectPath,
        string checkoutRoot,
        IReadOnlyCollection<string> evaluatedBuildInputs,
        IReadOnlyCollection<string> evaluatedMsBuildInputs,
        IReadOnlyDictionary<string, string> evaluatedGlobalProperties,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>? evaluatedProjectContexts,
        out string? gitRoot,
        out string? controlledProjectPath)
        => TryCreateControlledSourceCheckout(
            projectPath,
            checkoutRoot,
            evaluatedBuildInputs,
            evaluatedMsBuildInputs,
            evaluatedGlobalProperties,
            evaluatedProjectContexts,
            out gitRoot,
            out controlledProjectPath,
            out _);

    private static bool TryCreateControlledSourceCheckout(
        string projectPath,
        string checkoutRoot,
        IReadOnlyCollection<string> evaluatedBuildInputs,
        IReadOnlyCollection<string> evaluatedMsBuildInputs,
        IReadOnlyDictionary<string, string> evaluatedGlobalProperties,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>? evaluatedProjectContexts,
        out string? gitRoot,
        out string? controlledProjectPath,
        out string? failureReason)
    {
        gitRoot = null;
        controlledProjectPath = null;
        failureReason = null;
        try
        {
            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            gitRoot = ReadGitText(projectDirectory, "rev-parse --show-toplevel");
            string? revision = ReadGitText(projectDirectory, "rev-parse HEAD");
            if (string.IsNullOrWhiteSpace(gitRoot) ||
                string.IsNullOrWhiteSpace(revision) ||
                !IsSameOrBelowBuildInputPath(projectPath, gitRoot!))
            {
                failureReason = "project Git root or revision could not be resolved";
                return false;
            }

            string relativeProjectPath = FrameworkCompatibility.GetRelativePath(
                Path.GetFullPath(gitRoot!),
                Path.GetFullPath(projectPath));
            controlledProjectPath = Path.GetFullPath(Path.Combine(checkoutRoot, relativeProjectPath));
            if (!IsSameOrBelowBuildInputPath(controlledProjectPath, checkoutRoot))
            {
                failureReason = "controlled project path escaped the checkout root";
                return false;
            }

            if (!TryCollectControlledGitFilterNames(gitRoot!, revision!, out string[] filterNames))
            {
                failureReason = "Git filter configuration could not be inventoried";
                return false;
            }
            var checkout = RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                new[]
                {
                    "worktree",
                    "add",
                    "--detach",
                    checkoutRoot,
                    revision!
                },
                environmentVariables: null,
                TimeSpan.FromMinutes(2),
                BuildControlledGitConfiguration(filterNames));
            if (checkout.ExitCode != 0 || checkout.TimedOut || !File.Exists(controlledProjectPath))
            {
                string checkoutDetail = string.Join(" | ", checkout.StdErr
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .Reverse()
                    .Take(6)
                    .Reverse());
                failureReason = checkout.TimedOut
                    ? "detached Git checkout timed out"
                    : $"detached Git checkout failed with code {checkout.ExitCode}" +
                      (string.IsNullOrWhiteSpace(checkoutDetail)
                          ? string.Empty
                          : ": " + RedactCommandLineSecrets(checkoutDetail));
                return false;
            }
            if (!TryVerifyControlledGitConfiguration(
                    gitRoot!,
                    revision!,
                    filterNames) ||
                !TryVerifyControlledGitConfiguration(
                    checkoutRoot,
                    revision!,
                    filterNames))
            {
                failureReason = "Git filter configuration changed in the controlled checkout";
                return false;
            }

            if (!TryInitializeControlledSubmodules(checkoutRoot, filterNames))
            {
                failureReason = "controlled submodules could not be initialized";
                return false;
            }

            var controlledBuildInputs = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                controlledProjectPath
            };
            foreach (string input in evaluatedBuildInputs)
            {
                string fullInput = Path.GetFullPath(input);
                if (!IsSameOrBelowBuildInputPath(fullInput, gitRoot!))
                    continue;

                string relativeInput = FrameworkCompatibility.GetRelativePath(gitRoot!, fullInput);
                string controlledInput = Path.GetFullPath(Path.Combine(checkoutRoot, relativeInput));
                if (!IsSameOrBelowBuildInputPath(controlledInput, checkoutRoot))
                {
                    failureReason = "evaluated build input escaped the controlled checkout";
                    return false;
                }
                if (File.Exists(controlledInput))
                    controlledBuildInputs.Add(controlledInput);
            }

            var controlledMsBuildInputs = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                controlledProjectPath
            };
            foreach (string input in evaluatedMsBuildInputs)
            {
                string fullInput = Path.GetFullPath(input);
                if (!IsSameOrBelowBuildInputPath(fullInput, gitRoot!))
                    continue;
                string relativeInput = FrameworkCompatibility.GetRelativePath(gitRoot!, fullInput);
                string controlledInput = Path.GetFullPath(Path.Combine(checkoutRoot, relativeInput));
                if (!IsSameOrBelowBuildInputPath(controlledInput, checkoutRoot))
                {
                    failureReason = "evaluated MSBuild input escaped the controlled checkout";
                    return false;
                }
                if (File.Exists(controlledInput))
                    controlledMsBuildInputs.Add(controlledInput);
            }
            var controlledPropertyNames = new HashSet<string>(
                ReadControlledBuildPropertyNames(controlledMsBuildInputs),
                StringComparer.OrdinalIgnoreCase);

            var controlledGlobalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in evaluatedGlobalProperties)
            {
                if (!TryRemapControlledBuildValue(
                        property.Value,
                        gitRoot!,
                        checkoutRoot,
                        projectDirectory,
                        out string controlledValue))
                {
                    failureReason = $"global property '{property.Key}' could not be mapped";
                    return false;
                }
                controlledGlobalProperties[property.Key] = controlledValue;
            }

            var controlledProjectContexts = new Dictionary<
                string,
                IReadOnlyDictionary<string, string>[]>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>[]> project in
                     evaluatedProjectContexts ??
                     new Dictionary<string, IReadOnlyDictionary<string, string>[]>() )
            {
                string fullProjectPath = Path.GetFullPath(project.Key);
                if (!IsSameOrBelowBuildInputPath(fullProjectPath, gitRoot!))
                {
                    failureReason = "project evaluation context was outside the Git root";
                    return false;
                }
                string controlledContextProjectPath = Path.GetFullPath(Path.Combine(
                    checkoutRoot,
                    FrameworkCompatibility.GetRelativePath(gitRoot!, fullProjectPath)));
                if (!IsSameOrBelowBuildInputPath(controlledContextProjectPath, checkoutRoot) ||
                    !controlledMsBuildInputs.Contains(controlledContextProjectPath))
                {
                    failureReason = $"project evaluation context was not in the controlled MSBuild inputs: '{Path.GetFileName(fullProjectPath)}'";
                    return false;
                }

                var contexts = new List<IReadOnlyDictionary<string, string>>();
                foreach (IReadOnlyDictionary<string, string> context in project.Value)
                {
                    var controlledContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, string> property in context)
                    {
                        if (!controlledPropertyNames.Contains(property.Key))
                            continue;
                        // These are intrinsic to the independently selected dotnet/MSBuild
                        // toolchain. Replaying the original checkout's absolute SDK paths as
                        // project context would either escape the controlled checkout or let a
                        // source project redirect trusted tool execution. The controlled process
                        // resolves them from its verified dotnet host instead.
                        if (property.Key.Equals("MSBuildToolsPath", StringComparison.OrdinalIgnoreCase) ||
                            property.Key.Equals("MSBuildSDKsPath", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (!TryRemapControlledBuildValue(
                                property.Value,
                                gitRoot!,
                                checkoutRoot,
                            Path.GetDirectoryName(fullProjectPath)!,
                            out string controlledValue))
                        {
                            failureReason = $"project property '{property.Key}' could not be mapped for '{Path.GetFileName(fullProjectPath)}'";
                            return false;
                        }
                        controlledContext[property.Key] = controlledValue;
                    }
                    contexts.Add(controlledContext);
                }
                controlledProjectContexts[controlledContextProjectPath] = contexts.ToArray();
            }

            if (!HasOnlyControlledBuildFileInputs(
                    checkoutRoot,
                    controlledBuildInputs,
                    controlledMsBuildInputs,
                    controlledGlobalProperties,
                    Path.GetDirectoryName(controlledProjectPath!)!,
                    controlledProjectPath,
                    controlledProjectContexts,
                    out string? buildInputFailureReason))
            {
                failureReason = "controlled checkout contains an unverified build file input: " +
                    (buildInputFailureReason ?? "unknown reason");
                return false;
            }

            string? controlledRevision = ReadGitText(checkoutRoot, "rev-parse HEAD");
            if (!TryVerifyControlledGitConfiguration(
                    gitRoot!,
                    revision!,
                    filterNames) ||
                !TryVerifyControlledGitConfiguration(
                    checkoutRoot,
                    revision!,
                    filterNames))
            {
                failureReason = "Git filter configuration changed after controlled input validation";
                return false;
            }
            var controlledStatus = RunBuildInputEvaluationProcess(
                "git",
                checkoutRoot,
                new[]
                {
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all"
                },
                environmentVariables: null,
                TimeSpan.FromMinutes(1),
                BuildControlledGitConfiguration(filterNames));
            bool valid = string.Equals(revision, controlledRevision, StringComparison.OrdinalIgnoreCase) &&
                         controlledStatus.ExitCode == 0 &&
                         !controlledStatus.TimedOut &&
                         controlledStatus.StdOut.Length == 0;
            if (!valid)
                failureReason = "controlled checkout revision or clean-status verification failed";
            return valid;
        }
        catch (Exception exception)
        {
            failureReason = $"{exception.GetType().Name} while creating the controlled checkout";
            return false;
        }
    }

    private static bool TryVerifyControlledGitConfiguration(
        string workingDirectory,
        string revision,
        IReadOnlyCollection<string> expectedFilterNames)
        => TryCollectControlledGitFilterNames(
               workingDirectory,
               revision,
               out string[] currentFilterNames) &&
           currentFilterNames.SequenceEqual(expectedFilterNames, StringComparer.Ordinal);

    private static void RemoveControlledSourceCheckout(
        string? gitRoot,
        string checkoutRoot)
    {
        if (string.IsNullOrWhiteSpace(gitRoot))
            return;

        try
        {
            RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                new[] { "worktree", "remove", "--force", checkoutRoot },
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
        }
        catch
        {
            // The task-owned checkout is removed below and then pruned from Git metadata.
        }

        try
        {
            if (Directory.Exists(checkoutRoot))
                Directory.Delete(checkoutRoot, recursive: true);
        }
        catch
        {
            // Temporary checkout cleanup is best effort.
        }

        try
        {
            RunBuildInputEvaluationProcess(
                "git",
                gitRoot!,
                new[] { "worktree", "prune" },
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
        }
        catch
        {
            // Temporary worktree metadata cleanup is best effort.
        }
    }
}
