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
        string? sourceBlob = null,
        string? effectiveSourceExtension = null)
    {
        var sourceExtension = string.IsNullOrWhiteSpace(effectiveSourceExtension)
            ? Path.GetExtension(sourcePath)
            : effectiveSourceExtension!;
        if (!SourceIncludeExtensions.Contains(sourceExtension))
            return;
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var extension = sourceExtension;
        if (extension.Equals(".swift", StringComparison.OrdinalIgnoreCase) && !validateSwiftDeterminism)
            return;
        var validationKey = fullSourcePath + "|" + extension + "|" + validateSwiftDeterminism;
        if (!_validatedSourceIncludeFiles.Add(validationKey))
            return;
        if (!string.IsNullOrWhiteSpace(sourceBlob))
        {
            var semanticPath = ResolveSourceSemanticPath(fullSourcePath);
            var semanticKey = sourceBlob + "|" + semanticPath + "|" + extension + "|" + validateSwiftDeterminism;
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
        ValidateLanguageModuleImports(fullSourcePath, source, extension);
        RejectPreprocessorIncludeAliases(fullSourcePath, source);
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
                if (IsApprovedAngledInclude(repositoryRoot, fullSourcePath, include))
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

        ValidatePreprocessorFileExistenceProbes(repositoryRoot, fullSourcePath, source);

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

    private bool IsApprovedAngledInclude(string repositoryRoot, string sourcePath, string include)
    {
        var normalized = include.Replace('\\', '/').TrimStart('/');
        if (_inactiveRemoteSystemLibraryRoots.Any(root => IsPathAtOrWithin(sourcePath, root)))
            return true;
        var slash = normalized.IndexOf('/');
        if (slash < 0 && ApprovedToolchainHeaders.Contains(normalized))
            return true;
        if (slash >= 0 && ApprovedAppleSdkHeaderRoots.Contains(normalized.Substring(0, slash)))
            return true;

        var roots = new HashSet<string>(_approvedHeaderSearchRoots, GetPathComparer());
        for (var directory = Path.GetDirectoryName(sourcePath);
             !string.IsNullOrWhiteSpace(directory) && IsPathAtOrWithin(directory, repositoryRoot);
             directory = Path.GetDirectoryName(directory))
        {
            roots.Add(Path.Combine(directory, "include"));
            roots.Add(Path.Combine(directory, "Headers"));
            if (PathsEqual(new[] { directory }, new[] { repositoryRoot }))
                break;
        }

        var matches = roots
            .Where(Directory.Exists)
            .Select(root => Path.GetFullPath(Path.Combine(root, normalized)))
            .Where(File.Exists)
            .Distinct(GetPathComparer())
            .ToArray();
        if (matches.Length != 1)
            return false;
        EnsurePathWithinRepository(repositoryRoot, matches[0], $"angled preprocessor include from {sourcePath}");
        EnsureTrackedFile(repositoryRoot, matches[0], $"angled preprocessor include from {sourcePath}");
        return true;
    }

    private void ValidatePreprocessorFileExistenceProbes(string repositoryRoot, string sourcePath, string source)
    {
        var syntax = MaskCStringAndCharacterLiterals(source);
        var tokenPastedOperator = FindTokenPastedPreprocessorFileSelectionOperator(syntax);
        if (tokenPastedOperator is not null)
        {
            throw new InvalidOperationException(
                $"Source input '{sourcePath}' constructs preprocessor file-selection probe '{tokenPastedOperator}' through token pasting and cannot be bound to exact source.");
        }
        var embedProbe = Regex.Match(
            syntax,
            "(?<![A-Za-z0-9_])__has_embed[ \\t]*\\(",
            RegexOptions.CultureInvariant);
        if (embedProbe.Success)
        {
            throw new InvalidOperationException(
                $"Source input '{sourcePath}' uses a C23 __has_embed probe, whose file selection cannot be bound safely to the exact source commit.");
        }
        foreach (Match probe in Regex.Matches(
                     syntax,
                     "(?<![A-Za-z0-9_])__has_include(?:_next)?[ \\t]*\\(",
                     RegexOptions.CultureInvariant))
        {
            var opening = probe.Index + probe.Length - 1;
            var closing = FindMatchingCDelimiter(source, opening, '(', ')');
            var operand = source.Substring(opening + 1, closing - opening - 1).Trim();
            var quoted = operand.Length >= 2 && operand[0] == '\"' && operand[operand.Length - 1] == '\"';
            var angled = operand.Length >= 2 && operand[0] == '<' && operand[operand.Length - 1] == '>';
            if (!quoted && !angled)
            {
                throw new InvalidOperationException(
                    $"Source input '{sourcePath}' uses computed preprocessor file-existence probe '{operand}', which cannot be bound to exact source.");
            }

            var include = operand.Substring(1, operand.Length - 2).Trim();
            if (Path.IsPathRooted(include))
            {
                throw new InvalidOperationException(
                    $"Source input '{sourcePath}' probes absolute preprocessor input '{include}', which is outside the exact-source graph.");
            }
            if (angled)
            {
                if (!IsApprovedAngledInclude(repositoryRoot, sourcePath, include))
                    throw new InvalidOperationException($"Source input '{sourcePath}' probes unbound angled preprocessor input '{include}'.");
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, include));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"preprocessor file-existence probe from {sourcePath}");
            if (!File.Exists(candidate))
                throw new FileNotFoundException($"Preprocessor file-existence probe input was not found: {candidate}", candidate);
            EnsureTrackedFile(repositoryRoot, candidate, $"preprocessor file-existence probe from {sourcePath}");
        }
    }

    private static string? FindTokenPastedPreprocessorFileSelectionOperator(string syntax)
    {
        var tokenPastedSyntax = Regex.Replace(syntax, "[ \\t\\r\\n]*(?:##|%:%:)[ \\t\\r\\n]*", string.Empty);
        foreach (var operatorName in new[] { "__has_include", "__has_include_next", "__has_embed" })
        {
            if (!syntax.Contains(operatorName, StringComparison.Ordinal) &&
                tokenPastedSyntax.Contains(operatorName, StringComparison.Ordinal))
            {
                return operatorName;
            }
        }
        return null;
    }

    private static readonly HashSet<string> ApprovedToolchainHeaders = new(StringComparer.Ordinal)
    {
        "assert.h", "complex.h", "ctype.h", "errno.h", "fenv.h", "float.h", "inttypes.h", "iso646.h",
        "limits.h", "locale.h", "math.h", "setjmp.h", "signal.h", "stdalign.h", "stdarg.h", "stdatomic.h",
        "stdbool.h", "stddef.h", "stdint.h", "stdio.h", "stdlib.h", "stdnoreturn.h", "string.h", "tgmath.h",
        "threads.h", "time.h", "uchar.h", "wchar.h", "wctype.h",
        "metal_stdlib",
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
        "Accelerate", "AppKit", "AuthenticationServices", "AudioToolbox", "AVFoundation", "CFNetwork", "CloudKit", "CommonCrypto",
        "Compression", "Contacts", "CoreAudio", "CoreBluetooth", "CoreData", "CoreFoundation", "CoreGraphics",
        "CoreImage", "CoreLocation", "CoreMedia", "CoreMotion", "CoreServices", "CoreText", "CoreVideo",
        "CryptoKit", "Darwin", "DeviceCheck", "Dispatch", "EventKit", "Foundation", "GameController",
        "HealthKit", "HomeKit", "ImageIO", "IOKit", "LocalAuthentication", "MapKit", "Metal", "MetalKit",
        "Network", "NetworkExtension", "OSLog", "PassKit", "Photos", "QuartzCore", "SafariServices", "Security",
        "StoreKit", "SystemConfiguration", "UIKit", "UniformTypeIdentifiers", "UserNotifications", "VideoToolbox",
        "WatchKit", "WebKit", "ObjectiveC", "arpa", "dispatch", "libkern", "mach", "mach-o", "net", "netinet", "os", "simd",
        "sys", "xpc"
    };

    private static void ValidateLanguageModuleImports(string sourcePath, string source, string effectiveSourceExtension)
    {
        foreach (Match pragma in Regex.Matches(
                     source,
                     "(?<![A-Za-z0-9_])_Pragma\\s*\\(\\s*\\\"(?<payload>(?:\\\\.|[^\\\"\\\\])*)\\\"\\s*\\)",
                     RegexOptions.CultureInvariant))
        {
            var payload = Regex.Unescape(pragma.Groups["payload"].Value);
            var moduleImport = Regex.Match(
                payload,
                "^\\s*clang\\s+module\\s+import\\s+(?<module>[A-Za-z_][A-Za-z0-9_.]*)\\s*$",
                RegexOptions.CultureInvariant);
            if (moduleImport.Success)
                RejectUnapprovedLanguageModule(sourcePath, moduleImport.Groups["module"].Value, "Clang _Pragma");
        }
        if (Regex.IsMatch(
                source,
                "(?<![A-Za-z0-9_])_Pragma\\s*\\((?!\\s*\\\"(?:\\\\.|[^\\\"\\\\])*\\\"\\s*\\))",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"Source input '{sourcePath}' uses a computed _Pragma operand whose compiler behavior cannot be bound to the exact source commit.");
        }
        var syntax = MaskCStringAndCharacterLiterals(source);
        foreach (Match import in Regex.Matches(
                     syntax,
                     "(?<![A-Za-z0-9_])@import\\s+(?<module>[A-Za-z_][A-Za-z0-9_.]*)\\s*;",
                     RegexOptions.CultureInvariant))
        {
            var moduleName = import.Groups["module"].Value;
            var rootModule = moduleName.Split('.')[0];
            if (ApprovedAppleSdkHeaderRoots.Contains(rootModule))
                continue;
            throw new InvalidOperationException(
                $"Source input '{sourcePath}' imports Objective-C module '{moduleName}', whose module map and selected headers are not bound to an approved SDK, toolchain, or unique tracked module root.");
        }

        foreach (Match import in Regex.Matches(
                     syntax,
                     "(?m)^[ \\t]*(?:#|%:)[ \\t]*pragma[ \\t]+clang[ \\t]+module[ \\t]+import[ \\t]+(?<module>[A-Za-z_][A-Za-z0-9_.]*)",
                     RegexOptions.CultureInvariant))
        {
            RejectUnapprovedLanguageModule(sourcePath, import.Groups["module"].Value, "Clang pragma");
        }

        var extension = effectiveSourceExtension;
        if (!extension.Equals(".cc", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".cxx", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mm", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".hh", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".hxx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (Match import in Regex.Matches(
                     syntax,
                     "(?m)^[ \\t]*(?:export\\s+)?import\\s+(?<module>[^;]+?)\\s*;",
                     RegexOptions.CultureInvariant))
        {
            var moduleName = import.Groups["module"].Value.Trim();
            if (!Regex.IsMatch(
                    moduleName,
                    "^[A-Za-z_][A-Za-z0-9_.]*(?::[A-Za-z_][A-Za-z0-9_.]*)?$",
                    RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException(
                    $"Source input '{sourcePath}' uses C++ module or header-unit import '{moduleName}', whose selected bytes cannot be bound safely to the exact source commit.");
            }
            RejectUnapprovedLanguageModule(sourcePath, moduleName, "C++");
        }
    }

    private static void RejectPreprocessorIncludeAliases(string sourcePath, string source)
    {
        if (Regex.IsMatch(
                source,
                "(?m)^[ \\t]*(?:#|%:)[ \\t]*pragma[ \\t]+include_alias(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"Source input '{sourcePath}' uses pragma include_alias, whose replacement header can bypass the exact-source include graph.");
        }

        foreach (Match pragma in Regex.Matches(
                     source,
                     "(?<![A-Za-z0-9_])_Pragma\\s*\\(\\s*\\\"(?<payload>(?:\\\\.|[^\\\"\\\\])*)\\\"\\s*\\)",
                     RegexOptions.CultureInvariant))
        {
            var payload = Regex.Unescape(pragma.Groups["payload"].Value);
            if (!Regex.IsMatch(payload, "^\\s*include_alias(?![A-Za-z0-9_])", RegexOptions.CultureInvariant))
                continue;
            throw new InvalidOperationException(
                $"Source input '{sourcePath}' uses _Pragma include_alias, whose replacement header can bypass the exact-source include graph.");
        }
    }

    private static void RejectUnapprovedLanguageModule(string sourcePath, string moduleName, string syntax)
    {
        var rootModule = moduleName.Split('.', ':')[0];
        if (rootModule.Equals("std", StringComparison.Ordinal) ||
            ApprovedAppleSdkHeaderRoots.Contains(rootModule))
        {
            return;
        }
        throw new InvalidOperationException(
            $"Source input '{sourcePath}' imports {syntax} module '{moduleName}', whose module map and selected headers are not bound to an approved SDK, toolchain, or unique tracked module root.");
    }

    private static void RejectCTrigraphs(string source, string sourcePath)
    {
        var trigraph = Regex.Match(source, "\\?\\?[=/'()!<>-]", RegexOptions.CultureInvariant);
        if (!trigraph.Success)
            return;
        throw new InvalidOperationException(
            $"Source input '{sourcePath}' uses C trigraph '{trigraph.Value}', whose translation can change preprocessing semantics before exact-source validation.");
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

}
