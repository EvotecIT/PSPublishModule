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
                SplitBuildSettingPaths(value),
                key,
                generatedOutputPaths,
                source,
                responseFiles)
            .ToArray();
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

    private IEnumerable<string> ExpandCompilerResponseFileTokens(
        string repositoryRoot,
        string projectDirectory,
        IEnumerable<string> tokens,
        string key,
        IReadOnlyCollection<string> generatedOutputPaths,
        string source,
        ISet<string> responseFiles)
    {
        foreach (var token in tokens)
        {
            if (token.Length <= 1 ||
                token[0] != '@' ||
                token.StartsWith("@executable_path", StringComparison.Ordinal) ||
                token.StartsWith("@loader_path", StringComparison.Ordinal) ||
                token.StartsWith("@rpath", StringComparison.Ordinal))
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
                token.Equals("-force_load", StringComparison.Ordinal) ||
                token.Equals("-filelist", StringComparison.Ordinal) ||
                token.Equals("-ivfsoverlay", StringComparison.Ordinal) ||
                token.Equals("-vfsoverlay", StringComparison.Ordinal) ||
                token.Equals("-fplugin", StringComparison.Ordinal) ||
                token.Equals("-fpass-plugin", StringComparison.Ordinal) ||
                token.Equals("-load", StringComparison.Ordinal) ||
                token.Equals("-plugin", StringComparison.Ordinal) ||
                token.Equals("-plugin-path", StringComparison.Ordinal) ||
                token.Equals("-module-map-file", StringComparison.Ordinal) ||
                token.Equals("-isysroot", StringComparison.Ordinal) ||
                token.Equals("-sdk", StringComparison.Ordinal))
            {
                consumeNext = true;
                continue;
            }

            var prefixes = new[]
            {
                "-I", "-F", "-L", "-fmodule-map-file=", "-fplugin=", "-fpass-plugin=",
                "-ivfsoverlay=", "-vfsoverlay=", "-plugin-path=", "-module-map-file=",
                "-isysroot=", "-sdk="
            };
            var prefix = prefixes.FirstOrDefault(candidate =>
                token.StartsWith(candidate, StringComparison.Ordinal) && token.Length > candidate.Length);
            if (prefix is not null)
            {
                yield return token.Substring(prefix.Length);
                continue;
            }

            if (token.StartsWith("-D", StringComparison.Ordinal) ||
                token.StartsWith("-U", StringComparison.Ordinal) ||
                token.StartsWith("-Werror=", StringComparison.Ordinal) ||
                token.StartsWith("-Wno-", StringComparison.Ordinal) ||
                token.Contains("@executable_path", StringComparison.Ordinal) ||
                token.Contains("@loader_path", StringComparison.Ordinal) ||
                token.Contains("@rpath", StringComparison.Ordinal))
            {
                continue;
            }

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
        }
        if (consumeNext)
            throw new InvalidOperationException($"Xcode build setting {key} ends with a path-consuming flag and no input.");
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
