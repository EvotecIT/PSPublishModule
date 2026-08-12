namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static string ReadSwiftPluginExecutablePath(string value, string key)
    {
        var separator = value.IndexOf('#');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed Swift compiler plugin executable '{value}'. Expected <path>#<module-names>.");
        }
        return value.Substring(0, separator);
    }

    private static string[] ReadSwiftExternalPluginPaths(string value, string key)
    {
        var separator = value.IndexOf('#');
        if (separator <= 0 || separator == value.Length - 1 || value.IndexOf('#', separator + 1) >= 0)
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed Swift external plugin path '{value}'. Expected <search-path>#<plugin-server-path>.");
        }
        return new[] { value.Substring(0, separator), value.Substring(separator + 1) };
    }

    private static string[] ReadSwiftResolvedPluginPaths(string value, string key)
    {
        var parts = value.Split(new[] { '#' }, 3, StringSplitOptions.None);
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed Swift resolved plugin '{value}'. Expected <library-path>#<executable-path>#<module-names>.");
        }
        return new[] { parts[0], parts[1] };
    }

    private static string ReadSwiftModuleFilePath(string value, string key)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed Swift module input '{value}'. Expected <module-name>=<module-path>.");
        }
        var moduleName = value.Substring(0, separator);
        if (moduleName.Any(char.IsWhiteSpace) || IsPathLikeBuildFlagToken(moduleName))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains an invalid Swift module name in '{value}'.");
        }
        return value.Substring(separator + 1);
    }

    private static string ReadSwiftCrossImportPath(string moduleName, string value, string key)
    {
        if (string.IsNullOrWhiteSpace(moduleName) ||
            moduleName.Any(char.IsWhiteSpace) ||
            IsPathLikeBuildFlagToken(moduleName) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed Swift cross-import input. Expected <module-name> <overlay-path>.");
        }
        return value;
    }

    private static string ReadDylibOverrideCurrentPath(string value, string key)
    {
        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed linker dylib override '{value}'. Expected <install-path>:<current-path>.");
        }
        return value.Substring(separator + 1);
    }

    private static string[] ReadClangRemapFilePaths(string value, string key)
    {
        var parts = value.Split(new[] { ';' }, StringSplitOptions.None);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} contains malformed Clang remap input '{value}'. Expected <source-path>;<replacement-path>.");
        }
        return parts;
    }
}
