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
        if (!key.Split('[')[0].Trim().Equals("COMPILER_FLAGS", StringComparison.OrdinalIgnoreCase) &&
            TryReadCompilerLanguageOverride(expandedTokens, out _))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} uses a compiler language override outside a source-owned PBXBuildFile and cannot be bound safely.");
        }
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

    private static bool TryReadCompilerLanguageOverride(string[] tokens, out string? language)
    {
        language = null;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            string? candidate = null;
            if (token.Equals("-x", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException("Compiler option '-x' is missing its language argument.");
                candidate = tokens[index];
            }
            else if (token.StartsWith("-x", StringComparison.Ordinal) &&
                     token.Length > 2 &&
                     IsCompilerLanguageName(token.Substring(2)))
            {
                candidate = token.Substring(2);
            }
            if (candidate is null)
                continue;
            if (language is not null && !language.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PBX per-file compiler flags contain conflicting '-x' language overrides.");
            language = candidate;
        }
        return language is not null;
    }

    private static bool IsCompilerLanguageName(string value)
        => value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("c", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("c-header", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("cpp-output", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("c-cpp-output", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("objective-c", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("objective-c-header", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("objective-c-cpp-output", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("c++", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("c++-header", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("c++-cpp-output", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("objective-c++", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("objective-c++-header", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("objective-c++-cpp-output", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("assembler", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("assembler-with-cpp", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("cuda", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("hip", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("ir", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("cl", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("clcpp", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("renderscript", StringComparison.OrdinalIgnoreCase);

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

            if (token.Equals("--config", StringComparison.Ordinal) ||
                token.Equals("--config-user-dir", StringComparison.Ordinal) ||
                token.Equals("--config-system-dir", StringComparison.Ordinal) ||
                token.StartsWith("--config=", StringComparison.Ordinal) ||
                token.StartsWith("--config-user-dir=", StringComparison.Ordinal) ||
                token.StartsWith("--config-system-dir=", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Clang configuration-file option '{token}' in Xcode build setting {key} cannot be used by an exact-source Apple build.");
            }

            if (token.Equals("-load-plugin-executable", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with Swift compiler option '-load-plugin-executable' and no argument.");
                yield return ReadSwiftPluginExecutablePath(tokens[index], key);
                continue;
            }
            if (token.StartsWith("-load-plugin-executable=", StringComparison.Ordinal))
            {
                yield return ReadSwiftPluginExecutablePath(token.Substring("-load-plugin-executable=".Length), key);
                continue;
            }
            if (token.Equals("-external-plugin-path", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with Swift compiler option '-external-plugin-path' and no argument.");
                foreach (var path in ReadSwiftExternalPluginPaths(tokens[index], key))
                    yield return path;
                continue;
            }
            if (token.StartsWith("-external-plugin-path=", StringComparison.Ordinal))
            {
                foreach (var path in ReadSwiftExternalPluginPaths(token.Substring("-external-plugin-path=".Length), key))
                    yield return path;
                continue;
            }
            if (token.Equals("-load-resolved-plugin", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with Swift compiler option '-load-resolved-plugin' and no argument.");
                foreach (var path in ReadSwiftResolvedPluginPaths(tokens[index], key))
                    yield return path;
                continue;
            }
            if (token.StartsWith("-load-resolved-plugin=", StringComparison.Ordinal))
            {
                foreach (var path in ReadSwiftResolvedPluginPaths(token.Substring("-load-resolved-plugin=".Length), key))
                    yield return path;
                continue;
            }
            if (token.StartsWith("-swift-module-file=", StringComparison.Ordinal))
            {
                yield return ReadSwiftModuleFilePath(token.Substring("-swift-module-file=".Length), key);
                continue;
            }
            if (token.Equals("-swift-module-cross-import", StringComparison.Ordinal))
            {
                if (index + 2 >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends before Swift option '-swift-module-cross-import' receives its module and overlay path.");
                var moduleName = tokens[++index];
                yield return ReadSwiftCrossImportPath(moduleName, tokens[++index], key);
                continue;
            }
            if (token.Equals("-remap-file", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with Clang option '-remap-file' and no argument.");
                foreach (var path in ReadClangRemapFilePaths(tokens[index], key))
                    yield return path;
                continue;
            }
            if (token.StartsWith("-remap-file=", StringComparison.Ordinal))
            {
                foreach (var path in ReadClangRemapFilePaths(token.Substring("-remap-file=".Length), key))
                    yield return path;
                continue;
            }
            if (linkerFlags && token.Equals("-dylib_file", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with linker option '-dylib_file' and no argument.");
                yield return ReadDylibOverrideCurrentPath(tokens[index], key);
                continue;
            }
            if (linkerFlags && token.StartsWith("-dylib_file=", StringComparison.Ordinal))
            {
                yield return ReadDylibOverrideCurrentPath(token.Substring("-dylib_file=".Length), key);
                continue;
            }

            if (BuildFlagPathOptions.Contains(token))
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

            var prefix = BuildFlagPathPrefixes.FirstOrDefault(candidate =>
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
        var nondeterministicIdentifier = FindNondeterministicCompilerMacro(payload);
        if (nondeterministicIdentifier is not null)
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} supplies nondeterministic compiler identifier '{nondeterministicIdentifier}' through a preprocessor definition or undefinition.");
        }
        var valueSeparator = payload.IndexOf('=');
        var macroValue = valueSeparator >= 0 ? payload.Substring(valueSeparator + 1) : string.Empty;
        if (CanConstructAssemblerFileDirective(macroValue, parameters: null))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} supplies a preprocessor definition that can construct a file-consuming assembler directive: {payload}");
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
                token.Equals("-Xswiftc", StringComparison.Ordinal) ||
                token.Equals("-Xassembler", StringComparison.Ordinal) ||
                token.Equals("-Xpreprocessor", StringComparison.Ordinal) ||
                token.Equals("-Xclang", StringComparison.Ordinal))
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with forwarding option '{token}' and no argument.");
                expanded.Add(tokens[index]);
                continue;
            }

            if (token.StartsWith("-Wl,", StringComparison.Ordinal) ||
                token.StartsWith("-Wp,", StringComparison.Ordinal) ||
                token.StartsWith("-Wa,", StringComparison.Ordinal))
            {
                expanded.AddRange(token.Substring(4).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            foreach (var wrapper in new[]
                     {
                         "-Xcc=", "-Xlinker=", "-Xfrontend=", "-Xswiftc=",
                         "-Xassembler=", "-Xpreprocessor=", "-Xclang="
                     })
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
