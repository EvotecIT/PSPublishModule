namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly string[] HeaderSearchPathOptions =
    {
        "-I", "-F", "-iquote", "-isystem", "-isystem-after", "-idirafter",
        "-cxx-isystem", "-stdlib++-isystem", "-iframework", "-iframeworkwithsysroot"
    };

    private static readonly string[] LinkerSingleFileInputOptions =
    {
        "-order_file", "-exported_symbols_list", "-unexported_symbols_list",
        "-reexported_symbols_list", "-interposable_list", "-alias_list",
        "-force_load", "-weak_library", "-reexport_library", "-needed_library", "-bundle_loader"
    };

    private static void ValidateHeaderSearchPathInputs(string projectDirectory, string[] tokens, string key)
    {
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            string? value = null;
            var option = HeaderSearchPathOptions.FirstOrDefault(candidate => token.Equals(candidate, StringComparison.Ordinal));
            if (option is not null)
            {
                if (++index >= tokens.Length)
                    throw new InvalidOperationException($"Xcode build setting {key} ends with header search option '{option}' and no search root.");
                value = tokens[index];
            }
            else
            {
                option = HeaderSearchPathOptions
                    .OrderByDescending(static candidate => candidate.Length)
                    .FirstOrDefault(candidate => token.StartsWith(candidate, StringComparison.Ordinal) && token.Length > candidate.Length);
                if (option is not null)
                    value = token.Substring(option.Length).TrimStart('=');
            }
            if (string.IsNullOrWhiteSpace(value) ||
                IsValidatedToolchainOrBuildProductPath(value!, key, "header search option"))
                continue;
            var candidate = ResolveBuildSettingPath(projectDirectory, value!, key);
            RejectHeaderMapInput(candidate, key);
            if (File.Exists(candidate))
            {
                throw new InvalidOperationException(
                    $"Xcode build setting {key} uses file '{candidate}' as a header search root. Header-map and other file-backed search graphs are unsupported; use a tracked directory root instead.");
            }
        }
    }

    private static void RejectHeaderMapInput(string candidate, string key)
    {
        if (!Path.GetExtension(candidate).Equals(".hmap", StringComparison.OrdinalIgnoreCase))
            return;
        throw new InvalidOperationException(
            $"Xcode build setting {key} references header map '{candidate}', whose selected header paths cannot be bound to the exact source commit. Use tracked directory search roots instead.");
    }

    private static bool TryReadLinkerFileInputPaths(
        string[] tokens,
        ref int index,
        string token,
        string key,
        out string[] paths)
    {
        paths = Array.Empty<string>();
        var option = LinkerSingleFileInputOptions.FirstOrDefault(candidate => token.Equals(candidate, StringComparison.Ordinal));
        if (option is not null)
        {
            if (++index >= tokens.Length)
                throw new InvalidOperationException($"Xcode build setting {key} ends with linker option '{option}' and no file input.");
            paths = new[] { tokens[index] };
            return true;
        }

        option = LinkerSingleFileInputOptions.FirstOrDefault(candidate => token.StartsWith(candidate + "=", StringComparison.Ordinal));
        if (option is not null)
        {
            var path = token.Substring(option.Length + 1);
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"Xcode build setting {key} contains linker option '{option}' with an empty file input.");
            paths = new[] { path };
            return true;
        }

        if (!token.Equals("-sectorder", StringComparison.Ordinal) &&
            !token.Equals("-sectcreate", StringComparison.Ordinal))
            return false;
        if (index + 3 >= tokens.Length)
            throw new InvalidOperationException($"Xcode build setting {key} ends before linker option '{token}' receives its segment, section, and file input.");
        var segment = tokens[++index];
        var section = tokens[++index];
        if (IsPathLikeBuildFlagToken(segment) || IsPathLikeBuildFlagToken(section))
        {
            throw new InvalidOperationException(
                $"Linker option '{token}' in Xcode build setting {key} contains a path-like segment or section name.");
        }
        paths = new[] { tokens[++index] };
        return true;
    }
}
