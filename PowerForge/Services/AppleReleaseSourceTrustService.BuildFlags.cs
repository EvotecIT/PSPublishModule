namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateBuildFlagInputPaths(
        string repositoryRoot,
        string projectDirectory,
        string value,
        string key,
        IReadOnlyCollection<string> generatedOutputPaths,
        string source,
        ISet<string> responseFiles)
    {
        var expandedTokens = ExpandCompilerResponseFileTokens(
                repositoryRoot,
                projectDirectory,
                ExpandForwardedBuildFlagTokens(SplitBuildSettingPaths(value).ToArray(), key),
                key,
                generatedOutputPaths,
                source,
                responseFiles)
            .ToArray();
        ValidateLinkerFileLists(
            repositoryRoot,
            projectDirectory,
            expandedTokens,
            key,
            generatedOutputPaths,
            source);
        foreach (var rawValue in ExtractBuildFlagInputPaths(expandedTokens, key))
        {
            var normalizedValue = rawValue.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedValue))
                continue;
            if (IsValidatedToolchainOrBuildProductPath(normalizedValue, key, source))
                continue;

            var candidate = ResolveBuildSettingPath(projectDirectory, normalizedValue, key);
            EnsurePathWithinRepository(repositoryRoot, candidate, $"Xcode build setting {key} from {source}");
            EnsureNoGeneratedOutputOverlap(candidate, generatedOutputPaths, $"Xcode build setting {key}");
            if (File.Exists(candidate))
                EnsureTrackedFile(repositoryRoot, candidate, $"Xcode build setting {key}");
            else if (Directory.Exists(candidate))
                EnsureTrackedDirectoryTree(repositoryRoot, candidate, $"Xcode build setting {key}");
            else
                throw new FileNotFoundException(
                    $"Xcode build setting {key} references a missing exact-source input: {candidate}",
                    candidate);
        }
    }

    private void ValidateLinkerFileLists(
        string repositoryRoot,
        string projectDirectory,
        string[] inputTokens,
        string key,
        IReadOnlyCollection<string> generatedOutputPaths,
        string source)
    {
        var tokens = ExpandForwardedBuildFlagTokens(inputTokens, key);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!tokens[index].Equals("-filelist", StringComparison.Ordinal))
                continue;
            if (++index >= tokens.Length)
                throw new InvalidOperationException($"Xcode build setting {key} ends with linker option '-filelist' and no input.");

            var parts = tokens[index].Split(new[] { ',' }, 2, StringSplitOptions.None);
            var listPath = ResolveBuildSettingPath(projectDirectory, parts[0], key);
            EnsurePathWithinRepository(repositoryRoot, listPath, $"linker file list from {source}");
            EnsureNoGeneratedOutputOverlap(listPath, generatedOutputPaths, $"Xcode build setting {key} linker file list");
            if (!File.Exists(listPath))
                throw new FileNotFoundException($"Xcode build setting {key} references a missing linker file list: {listPath}", listPath);
            EnsureTrackedFile(repositoryRoot, listPath, $"Xcode build setting {key} linker file list");

            var inputRoot = parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1])
                ? ResolveBuildSettingPath(projectDirectory, parts[1], key)
                : projectDirectory;
            EnsurePathWithinRepository(repositoryRoot, inputRoot, $"linker file list base directory from {source}");
            foreach (var line in File.ReadAllLines(listPath))
            {
                var entry = line.Trim();
                if (entry.Length >= 2 && entry[0] == '"' && entry[entry.Length - 1] == '"')
                    entry = entry.Substring(1, entry.Length - 2).Replace("\\\"", "\"");
                if (string.IsNullOrWhiteSpace(entry))
                    continue;
                var candidate = ResolveBuildSettingPath(inputRoot, entry, key);
                EnsurePathWithinRepository(repositoryRoot, candidate, $"linker file list entry from {source}");
                EnsureNoGeneratedOutputOverlap(candidate, generatedOutputPaths, $"Xcode build setting {key} linker file list entry");
                if (!File.Exists(candidate))
                    throw new FileNotFoundException($"Xcode build setting {key} linker file list references a missing exact-source input: {candidate}", candidate);
                EnsureTrackedFile(repositoryRoot, candidate, $"Xcode build setting {key} linker file list entry");
            }
        }
    }

    private IEnumerable<string> ExpandCompilerResponseFileTokens(
        string repositoryRoot,
        string projectDirectory,
        IEnumerable<string> tokens,
        string key,
        IReadOnlyCollection<string> generatedOutputPaths,
        string source,
        ISet<string> responseFiles)
    {
        foreach (var token in ExpandForwardedBuildFlagTokens(tokens.ToArray(), key))
        {
            if (token.Length <= 1 ||
                token[0] != '@' ||
                IsAppleRuntimeRelativePath(token))
            {
                yield return token;
                continue;
            }

            var responseValue = token.Substring(1).Trim();
            var candidate = ResolveBuildSettingPath(projectDirectory, responseValue, key);
            EnsurePathWithinRepository(repositoryRoot, candidate, $"compiler response file from {source}");
            EnsureNoGeneratedOutputOverlap(candidate, generatedOutputPaths, $"Xcode build setting {key} response file");
            if (!File.Exists(candidate))
                throw new FileNotFoundException(
                    $"Xcode build setting {key} references a missing compiler response file: {candidate}",
                    candidate);
            EnsureTrackedFile(repositoryRoot, candidate, $"Xcode build setting {key} compiler response file");
            if (!responseFiles.Add(candidate))
                throw new InvalidOperationException(
                    $"Xcode build setting {key} contains a recursive compiler response-file cycle at '{candidate}'.");
            try
            {
                foreach (var nested in ExpandCompilerResponseFileTokens(
                             repositoryRoot,
                             projectDirectory,
                             SplitBuildSettingPaths(File.ReadAllText(candidate)),
                             key,
                             generatedOutputPaths,
                             $"response file {candidate}",
                             responseFiles))
                {
                    yield return nested;
                }
            }
            finally
            {
                responseFiles.Remove(candidate);
            }
        }
    }

    private static IEnumerable<string> ExtractBuildFlagInputPaths(string[] inputTokens, string key)
    {
        var tokens = ExpandForwardedBuildFlagTokens(inputTokens, key);
        var linkerFlags = key.Split('[')[0].Trim().Equals("OTHER_LDFLAGS", StringComparison.OrdinalIgnoreCase);
        var consumeNext = false;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (consumeNext)
            {
                consumeNext = false;
                yield return token;
                continue;
            }

            if (token.Equals("-I", StringComparison.Ordinal) ||
                token.Equals("-F", StringComparison.Ordinal) ||
                token.Equals("-L", StringComparison.Ordinal) ||
                token.Equals("-include", StringComparison.Ordinal) ||
                token.Equals("-imacros", StringComparison.Ordinal) ||
                token.Equals("-include-pch", StringComparison.Ordinal) ||
                token.Equals("-include-pth", StringComparison.Ordinal) ||
                token.Equals("-iquote", StringComparison.Ordinal) ||
                token.Equals("-isystem", StringComparison.Ordinal) ||
                token.Equals("-isystem-after", StringComparison.Ordinal) ||
                token.Equals("-idirafter", StringComparison.Ordinal) ||
                token.Equals("-iframework", StringComparison.Ordinal) ||
                token.Equals("-iframeworkwithsysroot", StringComparison.Ordinal) ||
                token.Equals("-iprefix", StringComparison.Ordinal) ||
                token.Equals("-iwithprefix", StringComparison.Ordinal) ||
                token.Equals("-iwithprefixbefore", StringComparison.Ordinal) ||
                token.Equals("-force_load", StringComparison.Ordinal) ||
                token.Equals("-ivfsoverlay", StringComparison.Ordinal) ||
                token.Equals("-vfsoverlay", StringComparison.Ordinal) ||
                token.Equals("-fplugin", StringComparison.Ordinal) ||
                token.Equals("-fpass-plugin", StringComparison.Ordinal) ||
                token.Equals("-load", StringComparison.Ordinal) ||
                token.Equals("-plugin", StringComparison.Ordinal) ||
                token.Equals("-plugin-path", StringComparison.Ordinal) ||
                token.Equals("-module-map-file", StringComparison.Ordinal) ||
                token.Equals("-fmodule-map-file", StringComparison.Ordinal) ||
                token.Equals("-fmodule-file", StringComparison.Ordinal) ||
                token.Equals("-fprofile-use", StringComparison.Ordinal) ||
                token.Equals("-fprofile-instr-use", StringComparison.Ordinal) ||
                token.Equals("-fprofile-sample-use", StringComparison.Ordinal) ||
                token.Equals("-resource-dir", StringComparison.Ordinal) ||
                token.Equals("-working-directory", StringComparison.Ordinal) ||
                token.Equals("-gcc-toolchain", StringComparison.Ordinal) ||
                token.Equals("--sysroot", StringComparison.Ordinal) ||
                token.Equals("-install_name", StringComparison.Ordinal) ||
                token.Equals("-rpath", StringComparison.Ordinal) ||
                token.Equals("-isysroot", StringComparison.Ordinal) ||
                token.Equals("-sdk", StringComparison.Ordinal))
            {
                consumeNext = true;
                continue;
            }


            if (token.Equals("-filelist", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with linker option '-filelist' and no input.");
                continue;
            }

            var prefixes = new[]
            {
                "-iframeworkwithsysroot", "-iwithprefixbefore", "-fprofile-instr-use=",
                "-fprofile-sample-use=", "-fmodule-map-file=", "-working-directory=",
                "-include-pch=", "-include-pth=", "-gcc-toolchain=", "-resource-dir=",
                "-iwithprefix", "-fprofile-use=", "-fmodule-file=", "-module-map-file=",
                "-ivfsoverlay=", "-vfsoverlay=", "-plugin-path=", "-fpass-plugin=",
                "-iframework", "-idirafter", "-imacros=", "-include=", "--sysroot=",
                "-isystem-after", "-isystem", "-iquote", "-iprefix", "-fplugin=", "-isysroot=", "-sdk=",
                "-I", "-F", "-L"
            };
            var prefix = prefixes.FirstOrDefault(candidate =>
                token.StartsWith(candidate, StringComparison.Ordinal) && token.Length > candidate.Length);
            if (prefix is not null)
            {
                var pathValue = token.Substring(prefix.Length);
                if (prefix.Equals("-fmodule-file=", StringComparison.Ordinal))
                {
                    var moduleSeparator = pathValue.IndexOf('=');
                    if (moduleSeparator >= 0)
                        pathValue = pathValue.Substring(moduleSeparator + 1);
                }
                yield return pathValue;
                continue;
            }

            if (token.Equals("-D", StringComparison.Ordinal) ||
                token.Equals("-U", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with preprocessor option '{token}' and no argument.");
                ValidatePreprocessorFlagPayload(tokens[index], key);
                continue;
            }

            if (token.StartsWith("-D", StringComparison.Ordinal) ||
                token.StartsWith("-U", StringComparison.Ordinal))
            {
                ValidatePreprocessorFlagPayload(token.Substring(2), key);
                continue;
            }

            if (token.StartsWith("-Werror=", StringComparison.Ordinal) ||
                token.StartsWith("-Wno-", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsAppleRuntimeRelativePath(token))
                continue;

            if (linkerFlags && TrySkipNonInputLinkerOption(tokens, ref index, token, key))
                continue;

            if (!linkerFlags && TrySkipNonInputCompilerOption(tokens, ref index, token, key))
                continue;

            if (token.StartsWith("-", StringComparison.Ordinal) && IsPathLikeBuildFlagToken(token))
            {
                throw new InvalidOperationException(
                    $"Path-bearing option in Xcode build setting {key} cannot be classified safely: {token}");
            }

            if (!token.StartsWith("-", StringComparison.Ordinal) &&
                IsPathLikeBuildFlagToken(token))
            {
                throw new InvalidOperationException(
                    $"Path-like token in Xcode build setting {key} cannot be classified safely: {token}");
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
                yield return token;
        }
        if (consumeNext)
            throw new InvalidOperationException($"Xcode build setting {key} ends with a path-consuming flag and no input.");
    }

    private static bool TrySkipNonInputCompilerOption(string[] tokens, ref int index, string token, string key)
    {
        var consumesOneValue = token.Equals("-arch", StringComparison.Ordinal) ||
                               token.Equals("-target", StringComparison.Ordinal) ||
                               token.Equals("--target", StringComparison.Ordinal) ||
                               token.Equals("-std", StringComparison.Ordinal) ||
                               token.Equals("-x", StringComparison.Ordinal) ||
                               token.Equals("-stdlib", StringComparison.Ordinal) ||
                               token.Equals("-module-name", StringComparison.Ordinal) ||
                               token.Equals("-swift-version", StringComparison.Ordinal) ||
                               token.Equals("-enforce-exclusivity", StringComparison.Ordinal) ||
                               token.Equals("-enable-experimental-feature", StringComparison.Ordinal) ||
                               token.Equals("-enable-upcoming-feature", StringComparison.Ordinal) ||
                               token.Equals("-strict-concurrency", StringComparison.Ordinal);
        if (!consumesOneValue)
            return false;
        if (++index >= tokens.Length)
            throw new InvalidOperationException($"Xcode build setting {key} ends with compiler option '{token}' and no argument.");
        var value = tokens[index];
        if (IsPathLikeBuildFlagToken(value))
        {
            throw new InvalidOperationException(
                $"Non-path compiler option '{token}' in Xcode build setting {key} contains a path-like argument: {value}");
        }
        return true;
    }

    private static bool TrySkipNonInputLinkerOption(string[] tokens, ref int index, string token, string key)
    {
        if (token.Equals("-framework", StringComparison.Ordinal) ||
            token.Equals("-weak_framework", StringComparison.Ordinal) ||
            token.Equals("-reexport_framework", StringComparison.Ordinal) ||
            token.Equals("-lazy_framework", StringComparison.Ordinal) ||
            token.Equals("-needed_framework", StringComparison.Ordinal))
        {
            if (++index >= tokens.Length)
                throw new InvalidOperationException($"Xcode build setting {key} ends with linker option '{token}' and no framework name.");
            throw new InvalidOperationException(
                $"Named framework '{tokens[index]}' in Xcode build setting {key} cannot be bound to an exact SDK, toolchain, or tracked framework input. " +
                "Use a validated PBX framework reference or an explicit approved-root framework path.");
        }

        var argumentCount = token switch
        {
            "-compatibility_version" => 1,
            "-current_version" => 1,
            "-arch" => 1,
            "-e" => 1,
            "-macos_version_min" => 1,
            "-ios_version_min" => 1,
            "-iphoneos_version_min" => 1,
            "-tvos_version_min" => 1,
            "-watchos_version_min" => 1,
            "-platform_version" => 3,
            _ => 0
        };
        if (argumentCount == 0)
            return false;
        if (index + argumentCount >= tokens.Length)
            throw new InvalidOperationException($"Xcode build setting {key} ends before linker option '{token}' receives all arguments.");
        index += argumentCount;
        return true;
    }

    private static void ValidatePreprocessorFlagPayload(string payload, string key)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException($"Xcode build setting {key} contains an empty preprocessor definition or undefinition.");
        if (payload.Contains("$(", StringComparison.Ordinal) ||
            payload.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains a preprocessor definition or undefinition with an unresolved build-setting reference: {payload}");
        }
    }

    private static string[] ExpandForwardedBuildFlagTokens(string[] tokens, string key)
    {
        var expanded = new List<string>(tokens.Length);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Equals("-Xcc", StringComparison.Ordinal) ||
                token.Equals("-Xlinker", StringComparison.Ordinal) ||
                token.Equals("-Xfrontend", StringComparison.Ordinal) ||
                token.Equals("-Xswiftc", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with forwarding option '{token}' and no argument.");
                expanded.Add(tokens[index]);
                continue;
            }

            if (token.StartsWith("-Wl,", StringComparison.Ordinal))
            {
                expanded.AddRange(token.Substring(4).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            foreach (var wrapper in new[] { "-Xcc=", "-Xlinker=", "-Xfrontend=", "-Xswiftc=" })
            {
                if (!token.StartsWith(wrapper, StringComparison.Ordinal))
                    continue;
                expanded.Add(token.Substring(wrapper.Length));
                token = string.Empty;
                break;
            }
            if (!string.IsNullOrEmpty(token))
                expanded.Add(token);
        }
        return expanded.ToArray();
    }

    private static bool IsPathLikeBuildFlagToken(string token)
        => Path.IsPathRooted(token) ||
           token.Contains('/') ||
           token.Contains('\\') ||
           token.Contains("$(", StringComparison.Ordinal) ||
           token.Contains("${", StringComparison.Ordinal);

    private static bool IsAppleRuntimeRelativePath(string token)
        => IsAppleRuntimeRelativePath(token, "@executable_path") ||
           IsAppleRuntimeRelativePath(token, "@loader_path") ||
           IsAppleRuntimeRelativePath(token, "@rpath");

    private static bool IsAppleRuntimeRelativePath(string token, string marker)
        => token.Equals(marker, StringComparison.Ordinal) ||
           (token.StartsWith(marker, StringComparison.Ordinal) &&
            token.Length > marker.Length &&
            token[marker.Length] == '/');

    private static bool IsValidatedToolchainOrBuildProductPath(
        string value,
        string key,
        string source)
    {
        var unownedBuildRoots = new[]
        {
            "$(BUILT_PRODUCTS_DIR)", "${BUILT_PRODUCTS_DIR}",
            "$(CONFIGURATION_BUILD_DIR)", "${CONFIGURATION_BUILD_DIR}",
            "$(TARGET_BUILD_DIR)", "${TARGET_BUILD_DIR}"
        };
        var unownedBuildRoot = unownedBuildRoots.FirstOrDefault(candidate =>
            value.Equals(candidate, StringComparison.Ordinal) ||
            (value.StartsWith(candidate, StringComparison.Ordinal) &&
             value.Length > candidate.Length &&
             (value[candidate.Length] == '/' || value[candidate.Length] == '\\')));
        if (unownedBuildRoot is not null)
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} consumes unowned build output '{unownedBuildRoot}', whose producing target and bytes cannot be proven at the exact source commit: {value} ({source})");
        }

        var known = new[]
        {
            "$(SDKROOT)", "${SDKROOT}",
            "$(DEVELOPER_DIR)", "${DEVELOPER_DIR}",
            "$(TOOLCHAIN_DIR)", "${TOOLCHAIN_DIR}"
        };
        var prefix = known.FirstOrDefault(candidate =>
            value.Equals(candidate, StringComparison.Ordinal) ||
            (value.StartsWith(candidate, StringComparison.Ordinal) &&
             value.Length > candidate.Length &&
             (value[candidate.Length] == '/' || value[candidate.Length] == '\\')));
        if (prefix is null)
            return false;

        var suffix = value.Substring(prefix.Length).Replace('\\', '/');
        if (suffix.Contains("$(", StringComparison.Ordinal) ||
            suffix.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} composes multiple build roots and cannot be proven safely: {value} ({source})");
        }

        var depth = 0;
        foreach (var segment in suffix.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (depth == 0)
                {
                    throw new InvalidOperationException(
                        $"Xcode build setting {key} escapes approved toolchain or build-product root '{prefix}': {value} ({source})");
                }
                depth--;
                continue;
            }
            depth++;
        }
        return true;
    }
}
