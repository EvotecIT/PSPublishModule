using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly HashSet<string> SourceIncludeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cxx", ".m", ".mm", ".metal", ".s",
        ".h", ".hh", ".hpp", ".hxx", ".inc", ".pch", ".modulemap", ".swift"
    };

    private void ValidateSourceLevelIncludes(
        string repositoryRoot,
        string sourcePath,
        bool validateSwiftDeterminism = false,
        string? sourceBlob = null)
    {
        if (!SourceIncludeExtensions.Contains(Path.GetExtension(sourcePath)))
            return;
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(fullSourcePath);
        if (extension.Equals(".swift", StringComparison.OrdinalIgnoreCase) && !validateSwiftDeterminism)
            return;
        if (!_validatedSourceIncludeFiles.Add(fullSourcePath))
            return;
        if (!string.IsNullOrWhiteSpace(sourceBlob))
        {
            var semanticPath = ResolveSourceSemanticPath(fullSourcePath);
            var semanticKey = sourceBlob + "|" + semanticPath + "|" + validateSwiftDeterminism;
            if (!_validatedSourceSemanticInputs.Add(semanticKey))
                return;
        }

        if (extension.Equals(".modulemap", StringComparison.OrdinalIgnoreCase))
        {
            ValidateClangModuleMapInputs(repositoryRoot, fullSourcePath);
            return;
        }
        if (extension.Equals(".swift", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSwiftSourceDeterminism(fullSourcePath);
            return;
        }

        // C and Objective-C splice escaped physical lines before comments and
        // preprocessing directives are interpreted. Scan that logical source so
        // an include keyword cannot be split across lines to evade attestation.
        var physicalSource = File.ReadAllText(fullSourcePath);
        RejectCTrigraphs(physicalSource, fullSourcePath);
        var source = RemoveCComments(SpliceCPreprocessingLines(physicalSource));
        var embedDirective = Regex.Match(
            source,
            "(?m)^[ \\t]*(?:#|%:)[ \\t]*embed(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        if (embedDirective.Success)
        {
            throw new InvalidOperationException(
                $"Source input '{fullSourcePath}' uses a C23 embed directive, whose payload selection cannot be bound safely to the exact source commit.");
        }
        var nondeterministicMacro = FindNondeterministicCompilerMacro(source);
        if (nondeterministicMacro is not null)
        {
            throw new InvalidOperationException(
                $"Source input '{fullSourcePath}' uses nondeterministic compiler macro '{nondeterministicMacro}', which cannot be bound to one reproducible source commit.");
        }
        foreach (Match directive in Regex.Matches(
                     source,
                     "(?m)^[ \\t]*(?:#|%:)[ \\t]*(?:include|include_next|import)[ \\t]+(?<operand>[^\\r\\n]+)",
                     RegexOptions.CultureInvariant))
        {
            var operand = Regex.Replace(directive.Groups["operand"].Value, "[ \\t]*(?://.*)?$", string.Empty).Trim();
            var quoted = operand.Length >= 2 && operand[0] == '"' && operand[operand.Length - 1] == '"';
            var angled = operand.Length >= 2 && operand[0] == '<' && operand[operand.Length - 1] == '>';
            if (!quoted && !angled)
            {
                throw new InvalidOperationException(
                    $"Source input '{fullSourcePath}' uses computed preprocessor include '{operand}', which cannot be bound to the exact source commit.");
            }

            var include = operand.Substring(1, operand.Length - 2).Trim();
            if (Path.IsPathRooted(include))
            {
                throw new InvalidOperationException(
                    $"Source input '{fullSourcePath}' references absolute preprocessor include '{include}', which is outside the exact-source graph.");
            }

            var segments = include.Split('/', '\\');
            if (angled)
            {
                if (segments.Any(static segment => segment == ".."))
                    throw new InvalidOperationException($"Source input '{fullSourcePath}' uses escaping system include '{include}'.");
                if (IsApprovedAngledInclude(repositoryRoot, include))
                    continue;
                throw new InvalidOperationException(
                    $"Source input '{fullSourcePath}' uses angled preprocessor include '{include}', whose selected bytes depend on unbound compiler search roots. " +
                    "Use a tracked quoted include or a validated Xcode module/framework reference instead.");
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullSourcePath)!, include));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"preprocessor include from {fullSourcePath}");
            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException(
                    $"Quoted preprocessor include '{include}' from '{fullSourcePath}' was not found beside the including source. " +
                    "Its compiler search-path selection cannot be bound to the exact source commit; use an adjacent tracked include or an approved module/framework reference.",
                    candidate);
            }
            EnsureTrackedFile(repositoryRoot, candidate, $"preprocessor include from {fullSourcePath}");
        }

        if (IsCInlineAssemblySource(extension))
            ValidateInlineAssemblerInputs(repositoryRoot, fullSourcePath, source);

        if (extension.Equals(".s", StringComparison.OrdinalIgnoreCase))
            ValidateAssemblerInputs(repositoryRoot, fullSourcePath, source);
    }

    private static string ResolveSourceSemanticPath(string sourcePath)
    {
        var normalized = sourcePath.Replace('\\', '/');
        var framework = normalized.IndexOf(".xcframework/", StringComparison.OrdinalIgnoreCase);
        if (framework < 0)
            return normalized;
        var headers = normalized.IndexOf("/Headers/", framework, StringComparison.OrdinalIgnoreCase);
        return headers < 0 ? normalized : normalized.Substring(headers + "/Headers/".Length);
    }

    private bool IsApprovedAngledInclude(string repositoryRoot, string include)
    {
        var normalized = include.Replace('\\', '/').TrimStart('/');
        if (!_trackedSourceSuffixes.TryGetValue(repositoryRoot, out var trackedSuffixes))
        {
            trackedSuffixes = new HashSet<string>(GetPathComparer());
            var tracked = RunGit(repositoryRoot, "ls-files", "-z").StdOut
                .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static path => path.Replace('\\', '/'))
                .ToArray();
            foreach (var path in tracked)
            {
                trackedSuffixes.Add(path);
                for (var index = path.IndexOf('/'); index >= 0; index = path.IndexOf('/', index + 1))
                    trackedSuffixes.Add(path.Substring(index + 1));
            }
            _trackedSourceSuffixes[repositoryRoot] = trackedSuffixes;
        }
        if (trackedSuffixes.Contains(normalized))
            return true;

        var slash = normalized.IndexOf('/');
        if (slash < 0)
            return ApprovedToolchainHeaders.Contains(normalized);
        return ApprovedAppleSdkHeaderRoots.Contains(normalized.Substring(0, slash));
    }

    private static readonly HashSet<string> ApprovedToolchainHeaders = new(StringComparer.Ordinal)
    {
        "assert.h", "complex.h", "ctype.h", "errno.h", "fenv.h", "float.h", "inttypes.h", "iso646.h",
        "limits.h", "locale.h", "math.h", "setjmp.h", "signal.h", "stdalign.h", "stdarg.h", "stdatomic.h",
        "stdbool.h", "stddef.h", "stdint.h", "stdio.h", "stdlib.h", "stdnoreturn.h", "string.h", "tgmath.h",
        "threads.h", "time.h", "uchar.h", "wchar.h", "wctype.h",
        "algorithm", "array", "atomic", "bit", "bitset", "cassert", "cctype", "cerrno", "cfenv", "cfloat",
        "charconv", "chrono", "cinttypes", "climits", "cmath", "complex", "concepts", "condition_variable",
        "coroutine", "cstddef", "cstdint", "cstdio", "cstdlib", "cstring", "deque", "exception", "filesystem",
        "format", "forward_list", "fstream", "functional", "future", "initializer_list", "iomanip", "ios", "iosfwd",
        "iostream", "istream", "iterator", "latch", "limits", "list", "map", "memory", "memory_resource", "mutex",
        "new", "numbers", "numeric", "optional", "ostream", "queue", "random", "ranges", "ratio", "regex",
        "scoped_allocator", "semaphore", "set", "shared_mutex", "source_location", "span", "sstream", "stack",
        "stdexcept", "stop_token", "streambuf", "string", "string_view", "syncstream", "system_error", "thread",
        "tuple", "type_traits", "typeindex", "typeinfo", "unordered_map", "unordered_set", "utility", "valarray",
        "variant", "vector", "version"
    };

    private static readonly HashSet<string> ApprovedAppleSdkHeaderRoots = new(StringComparer.Ordinal)
    {
        "Accelerate", "AppKit", "AudioToolbox", "AVFoundation", "CFNetwork", "CloudKit", "CommonCrypto",
        "Compression", "Contacts", "CoreAudio", "CoreBluetooth", "CoreData", "CoreFoundation", "CoreGraphics",
        "CoreImage", "CoreLocation", "CoreMedia", "CoreMotion", "CoreServices", "CoreText", "CoreVideo",
        "CryptoKit", "Darwin", "DeviceCheck", "Dispatch", "EventKit", "Foundation", "GameController",
        "HealthKit", "HomeKit", "ImageIO", "IOKit", "LocalAuthentication", "MapKit", "Metal", "MetalKit",
        "Network", "NetworkExtension", "OSLog", "PassKit", "Photos", "QuartzCore", "SafariServices", "Security",
        "StoreKit", "SystemConfiguration", "UIKit", "UniformTypeIdentifiers", "UserNotifications", "VideoToolbox",
        "WatchKit", "WebKit", "arpa", "dispatch", "libkern", "mach", "mach-o", "net", "netinet", "os", "simd",
        "sys", "xpc"
    };

    private static void RejectCTrigraphs(string source, string sourcePath)
    {
        var trigraph = Regex.Match(source, "\\?\\?[=/'()!<>-]", RegexOptions.CultureInvariant);
        if (!trigraph.Success)
            return;
        throw new InvalidOperationException(
            $"Source input '{sourcePath}' uses C trigraph '{trigraph.Value}', whose translation can change preprocessing semantics before exact-source validation.");
    }

    private void ValidateAssemblerInputs(string repositoryRoot, string sourcePath, string source)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!_validatedAssemblerInputFiles.Add(fullSourcePath))
            return;

        ValidateAssemblerDirectives(repositoryRoot, fullSourcePath, source);
    }

    private void ValidateAssemblerDirectives(string repositoryRoot, string sourcePath, string source)
    {

        foreach (Match directive in Regex.Matches(
                     source,
                     "(?im)^[ \\t]*\\.(?<kind>include|incbin)(?![A-Za-z0-9_])[ \\t]+(?<operand>[^\\r\\n]+)",
                     RegexOptions.CultureInvariant))
        {
            var operand = directive.Groups["operand"].Value.Trim();
            var literal = Regex.Match(operand, "^\\\"(?<path>[^\\\"\\\\]*)\\\"", RegexOptions.CultureInvariant);
            if (!literal.Success)
            {
                throw new InvalidOperationException(
                    $"Assembler source input '{sourcePath}' uses computed .{directive.Groups["kind"].Value} input '{operand}', which cannot be bound to the exact source commit.");
            }

            var input = literal.Groups["path"].Value;
            if (Path.IsPathRooted(input))
            {
                throw new InvalidOperationException(
                    $"Assembler source input '{sourcePath}' references absolute .{directive.Groups["kind"].Value} input '{input}', which is outside the exact-source graph.");
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, input));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"assembler .{directive.Groups["kind"].Value} input from {sourcePath}");
            if (!File.Exists(candidate))
                throw new FileNotFoundException($"Assembler input was not found inside the exact checked-out source: {candidate}", candidate);
            EnsureTrackedFile(repositoryRoot, candidate, $"assembler .{directive.Groups["kind"].Value} input from {sourcePath}");
            if (directive.Groups["kind"].Value.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                var nestedPhysicalSource = File.ReadAllText(candidate);
                RejectCTrigraphs(nestedPhysicalSource, candidate);
                var nestedSource = RemoveCComments(SpliceCPreprocessingLines(nestedPhysicalSource));
                ValidateAssemblerInputs(repositoryRoot, candidate, nestedSource);
            }
        }
    }

    private static void ValidateSwiftSourceDeterminism(string sourcePath)
    {
        var contents = File.ReadAllText(sourcePath);
        if (contents.IndexOf("#file", StringComparison.Ordinal) < 0)
            return;
        var syntax = MaskSwiftStringLiterals(RemoveSwiftComments(contents));
        var locationLiteral = Regex.Match(
            syntax,
            "(?<![A-Za-z0-9_])#(?<literal>file|filePath)(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        if (!locationLiteral.Success)
            return;
        throw new InvalidOperationException(
            $"Swift source input '{sourcePath}' uses snapshot-path compiler literal '#{locationLiteral.Groups["literal"].Value}', " +
            "which exposes changing checkout or host state and cannot be bound to one reproducible detached source location. " +
            "Use #fileID or an explicit stable identifier instead.");
    }

    private void ValidateClangModuleMapInputs(string repositoryRoot, string moduleMapPath)
    {
        var source = RemoveCComments(File.ReadAllText(moduleMapPath));
        var unboundLink = Regex.Match(
            source,
            "(?<![A-Za-z0-9_])link\\s+(?:framework\\s+)?\\\"(?<name>(?:\\\\.|[^\\\"\\\\])*)\\\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (unboundLink.Success &&
            !_inactiveRemoteSystemLibraryRoots.Any(root => IsPathAtOrWithin(moduleMapPath, root)))
        {
            throw new InvalidOperationException(
                $"Clang module map '{moduleMapPath}' declares unbound autolink '{unboundLink.Value.Trim()}', whose SDK or library bytes cannot be proven at the exact source commit.");
        }
        const string declaration =
            "(?<![A-Za-z0-9_])(?:" +
            "(?:(?:private|textual)\\s+)*header|" +
            "umbrella(?:\\s+header)?|" +
            "exclude\\s+header|" +
            "extern\\s+module\\s+[A-Za-z_][A-Za-z0-9_.]*" +
            ")\\s*\"(?<path>(?:\\\\.|[^\"\\\\])*)\"";
        foreach (Match match in Regex.Matches(
                     source,
                     declaration,
                     RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            var declaredPath = match.Groups["path"].Value;
            if (declaredPath.Contains('\\'))
            {
                throw new InvalidOperationException(
                    $"Clang module map '{moduleMapPath}' uses an escaped or platform-dependent input path '{declaredPath}', which cannot be attested safely.");
            }
            if (Path.IsPathRooted(declaredPath))
            {
                throw new InvalidOperationException(
                    $"Clang module map '{moduleMapPath}' references absolute input '{declaredPath}', which is outside the exact-source graph.");
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(moduleMapPath)!, declaredPath));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"Clang module map input from {moduleMapPath}");
            if (File.Exists(candidate))
            {
                EnsureTrackedFile(repositoryRoot, candidate, $"Clang module map input from {moduleMapPath}");
            }
            else if (Directory.Exists(candidate))
            {
                EnsureTrackedDirectoryTree(repositoryRoot, candidate, $"Clang module map input from {moduleMapPath}");
            }
            else
            {
                throw new FileNotFoundException(
                    $"Clang module map input was not found inside the exact checked-out source: {candidate}",
                    candidate);
            }
        }
    }

    private static string? FindNondeterministicCompilerMacro(string source)
    {
        var masked = MaskCStringAndCharacterLiterals(source);
        var found = FindNondeterministicCompilerIdentifier(masked);
        if (found is not null)
            return found;

        var tokenPasted = Regex.Replace(masked, "[ \\t\\r\\n]*(?:##|%:%:)[ \\t\\r\\n]*", string.Empty);
        return FindNondeterministicCompilerIdentifier(tokenPasted);
    }

    private static string? FindNondeterministicCompilerIdentifier(string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '_' && !char.IsLetter(source[index]))
                continue;
            var start = index;
            while (index + 1 < source.Length &&
                   (source[index + 1] == '_' || char.IsLetterOrDigit(source[index + 1])))
                index++;
            var identifier = source.Substring(start, index - start + 1);
            if (identifier is "__DATE__" or "__TIME__" or "__TIMESTAMP__" or
                "__FILE__" or "__BASE_FILE__" or "__builtin_FILE" or
                "__builtin_source_location" or "source_location")
                return identifier;
        }
        return null;
    }

    private static string MaskCStringAndCharacterLiterals(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var quote = '\0';
        var escaped = false;
        foreach (var current in source)
        {
            if (quote == '\0')
            {
                if (current is '\"' or '\'')
                {
                    quote = current;
                    result.Append(' ');
                }
                else
                {
                    result.Append(current);
                }
                continue;
            }

            result.Append(current is '\r' or '\n' ? current : ' ');
            if (escaped)
                escaped = false;
            else if (current == '\\')
                escaped = true;
            else if (current == quote)
                quote = '\0';
        }
        return result.ToString();
    }

    private static string SpliceCPreprocessingLines(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '\\' || index + 1 >= source.Length)
            {
                result.Append(source[index]);
                continue;
            }

            if (source[index + 1] == '\n')
            {
                index++;
                continue;
            }
            if (source[index + 1] == '\r')
            {
                index++;
                if (index + 1 < source.Length && source[index + 1] == '\n')
                    index++;
                continue;
            }

            result.Append(source[index]);
        }
        return result.ToString();
    }

    private static string RemoveCComments(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var inBlockComment = false;
        var inLineComment = false;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                    result.Append(current);
                }
                else
                {
                    result.Append(' ');
                }
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    result.Append("  ");
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    result.Append(current == '\r' || current == '\n' ? current : ' ');
                }
                continue;
            }
            if (quote != '\0')
            {
                result.Append(current);
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current == '/' && next == '/')
            {
                result.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }
            if (current == '/' && next == '*')
            {
                result.Append("  ");
                index++;
                inBlockComment = true;
                continue;
            }
            if (current == '"' || current == '\'')
                quote = current;
            result.Append(current);
        }
        return result.ToString();
    }
}
