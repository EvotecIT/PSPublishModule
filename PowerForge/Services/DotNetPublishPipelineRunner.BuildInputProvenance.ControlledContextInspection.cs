using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    // Preprocessing evaluates imports without executing targets (including InitialTargets).
    // Keep import semantics in the selected SDK, then apply our controlled-input policy
    // to its actual import inventory before allowing the invocation to execute.
    private static bool TryInspectControlledContextInvocation(
        string workingDirectory, IReadOnlyList<string> invocation,
        IReadOnlyDictionary<string, string?> environment, string outputRoot,
        out string? failureReason)
    {
        failureReason = null;
        string preprocessPath = Path.Combine(outputRoot, "context-inspection-" + Guid.NewGuid().ToString("N") + ".xml");
        string responsePath = preprocessPath + ".rsp";
        try
        {
            var evaluation = invocation.Where(argument =>
                !argument.StartsWith("-target:", StringComparison.OrdinalIgnoreCase) &&
                !argument.StartsWith("-getItem:", StringComparison.OrdinalIgnoreCase) &&
                !argument.StartsWith("-getProperty:", StringComparison.OrdinalIgnoreCase) &&
                !argument.Equals("-restore", StringComparison.OrdinalIgnoreCase)).ToList();
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] requestedTargets = invocation.Where(argument => argument.StartsWith("-target:", StringComparison.OrdinalIgnoreCase))
                .SelectMany(argument => DecodeMsBuildEscapes(argument.Substring(8)).Split(';', ','))
                .Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
            foreach (string argument in evaluation.Where(argument => argument.StartsWith("-p:", StringComparison.OrdinalIgnoreCase)))
            {
                int separator = argument.IndexOf('=');
                if (separator > 3)
                    globals[argument.Substring(3, separator - 3)] = DecodeMsBuildEscapes(argument.Substring(separator + 1));
            }
            string wrapper = globals["DirectoryBuildPropsPath"];
            // The owner appends its exact randomized verifier to Build; it does not
            // add an SDK execution entry point. Keep all other requested roots.
            string verifierTarget = BuildControlledRestoreContextVerifierTargetNameFromProps(wrapper);
            requestedTargets = requestedTargets.Where(name => !name.Equals(verifierTarget, StringComparison.Ordinal)).ToArray();
            string fileName = Path.GetFileNameWithoutExtension(wrapper);
            string matchedName = "_PowerForgeMatched_" + fileName.Substring(fileName.LastIndexOf('.') + 1);
            XDocument wrapperDocument = XDocument.Load(wrapper);
            var presenceProofs = wrapperDocument.Descendants("_PowerForgeControlledPresenceProperty")
                .Where(element => FileSystemPathSafety.ExistingPathComparer.Equals(
                    DecodeMsBuildEscapes(element.Element("ProjectPath")?.Value ?? string.Empty), Path.GetFullPath(invocation[1])))
                .Select(element => (Name: element.Attribute("Include")!.Value, Result: element.Element("ResultProperty")!.Value)).ToArray();
            // Query evaluated values through MSBuild as well; never execute a target to
            // obtain this information, since that is precisely what the guard protects.
            var queries = evaluation.Concat(new[] { "-getProperty:MSBuildToolsPath", "-getProperty:MSBuildSDKsPath", "-getProperty:NuGetPackageRoot", "-getProperty:" + matchedName })
                .Concat(new[] { "BuildDependsOn", "RebuildDependsOn", "CleanDependsOn", "GetTargetPathDependsOn", "ComputeFilesToPublishDependsOn", "GeneratePackageOnBuild" }.Select(name => "-getProperty:" + name))
                .Concat(ControlledContextPathProperties.Select(name => "-getProperty:" + name))
                .Concat(presenceProofs.Select(proof => "-getProperty:" + proof.Result)).ToArray();
            File.WriteAllLines(responsePath, queries, new UTF8Encoding(false));
            var propertiesProcess = RunBuildInputEvaluationProcess("dotnet", workingDirectory,
                ["@" + responsePath], environment, TimeSpan.FromMinutes(2));
            if (propertiesProcess.ExitCode != 0 || propertiesProcess.TimedOut)
            {
                failureReason = "MSBuild could not evaluate the controlled context without executing targets: " + ReadControlledProcessFailureDetail(propertiesProcess);
                return false;
            }
            int start = propertiesProcess.StdOut.IndexOf('{');
            int end = propertiesProcess.StdOut.LastIndexOf('}');
            using JsonDocument result = JsonDocument.Parse(propertiesProcess.StdOut.Substring(start, end - start + 1));
            var properties = result.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            string[] toolchainRoots = ReadTrustedBuildInfrastructureRoots(result.RootElement.GetProperty("Properties"), workingDirectory);
            foreach (var proof in presenceProofs)
                if (!properties.TryGetValue(proof.Result, out string? supplied) ||
                    supplied != (globals.ContainsKey(proof.Name) ? "true" : "false"))
                {
                    failureReason = $"restore-context property '{proof.Name}' does not preserve global-property presence; globals declared as local cannot use this isolation proof.";
                    return false;
                }
            File.WriteAllLines(responsePath, evaluation.Append("-preprocess:" + preprocessPath), new UTF8Encoding(false));
            var process = RunBuildInputEvaluationProcess("dotnet", workingDirectory,
                ["@" + responsePath], environment, TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut || !File.Exists(preprocessPath))
            {
                failureReason = "MSBuild could not preprocess the controlled context: " + ReadControlledProcessFailureDetail(process);
                return false;
            }
            var paths = new HashSet<string>(FileSystemPathSafety.ExistingPathComparer) { Path.GetFullPath(invocation[1]) };
            bool describesImport = false;
            foreach (string line in File.ReadLines(preprocessPath))
            {
                if (line.Contains("<!--", StringComparison.Ordinal)) describesImport = false;
                if (line.Contains("<Import", StringComparison.Ordinal)) describesImport = true;
                if (describesImport && Path.IsPathRooted(line.Trim()) && File.Exists(line.Trim())) paths.Add(Path.GetFullPath(line.Trim()));
                if (line.Contains("-->", StringComparison.Ordinal)) describesImport = false;
            }
            var documents = new Dictionary<string, XDocument>(FileSystemPathSafety.ExistingPathComparer);
            var toolchainNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (toolchainRoots.Any(root => IsSameOrBelowBuildInputPath(path, root)))
                {
                    XDocument sdkDocument = XDocument.Load(path);
                    foreach (XElement target in sdkDocument.Descendants().Where(element => element.Name.LocalName == "Target"))
                        if (target.Attribute("Name") is XAttribute name) toolchainNames.Add(name.Value);
                    // Keep the existing conservative policy for custom targets reached
                    // through SDK default dependency properties, including hookless ones.
                    foreach (string value in sdkDocument.Descendants()
                                 .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
                                 .SelectMany(element => element.Value.Split(';')))
                        if (IsValidControlledMsBuildPropertyName(value.Trim())) toolchainNames.Add(value.Trim());
                    continue;
                }
                // Everything besides the selected SDK must belong to this private
                // checkout/cache. An absolute import into the mutable source is rejected.
                if (!IsSameOrBelowBuildInputPath(path, outputRoot) || HasReparsePointBelowRoot(path, outputRoot))
                {
                    failureReason = $"controlled context imports a file outside its private checkout/cache: '{path}'.";
                    return false;
                }
                XDocument document = XDocument.Load(path);
                // Imports are already authoritatively evaluated, so the task checker
                // should inspect executable content, not attempt to resolve them again.
                document.Descendants().Where(element => element.Name.LocalName == "Import").Remove();
                documents[path] = document;
            }
            var customDocuments = documents.Where(pair => !FileSystemPathSafety.ExistingPathComparer.Equals(pair.Key, wrapper)).ToArray();
            // Ask MSBuild to expand custom dependency aliases, rather than resolving
            // their expressions here. Only custom documents add dependency roots;
            // unrelated SDK targets must not make an unused Pack/Publish hook active.
            string[] extraDependencies = customDocuments.SelectMany(pair => pair.Value.Descendants())
                .Where(element => element.Parent?.Name.LocalName == "PropertyGroup" &&
                    IsControlledContextDependencyProperty(element.Name.LocalName) && !properties.ContainsKey(element.Name.LocalName))
                .Select(element => element.Name.LocalName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (extraDependencies.Length > 0)
            {
                File.WriteAllLines(responsePath, evaluation.Concat(extraDependencies.Select(name => "-getProperty:" + name))
                    .Append("-getProperty:" + matchedName), new UTF8Encoding(false));
                var dependenciesProcess = RunBuildInputEvaluationProcess("dotnet", workingDirectory,
                    ["@" + responsePath], environment, TimeSpan.FromMinutes(2));
                if (dependenciesProcess.ExitCode != 0 || dependenciesProcess.TimedOut)
                {
                    failureReason = "MSBuild could not evaluate controlled target dependencies: " + ReadControlledProcessFailureDetail(dependenciesProcess);
                    return false;
                }
                int dependencyStart = dependenciesProcess.StdOut.IndexOf('{');
                int dependencyEnd = dependenciesProcess.StdOut.LastIndexOf('}');
                using JsonDocument dependencies = JsonDocument.Parse(dependenciesProcess.StdOut.Substring(dependencyStart, dependencyEnd - dependencyStart + 1));
                foreach (var property in dependencies.RootElement.GetProperty("Properties").EnumerateObject())
                    properties[property.Name] = property.Value.GetString() ?? string.Empty;
            }
            IReadOnlyDictionary<string, string> immutableGlobals = ReadImmutableTargetGuardProperties(globals, paths);
            var trustedIdentities = wrapperDocument.Descendants("_PowerForgeControlledTrustedPackage")
                .GroupBy(element => DecodeMsBuildEscapes(element.Attribute("Include")?.Value ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Element("ContentHash")?.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            bool IsAdmittedTrustedPackageInput(string path)
            {
                foreach (string? root in new[] { properties.TryGetValue("NuGetPackageRoot", out string? packageRoot) ? packageRoot : null,
                             environment.TryGetValue("NUGET_PACKAGES", out string? environmentRoot) ? environmentRoot : null })
                {
                    if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root!) || !IsSameOrBelowBuildInputPath(path, root!)) continue;
                    string[] segments = FrameworkCompatibility.GetRelativePath(root!, path).Replace('\\', '/').Split('/');
                    if (segments.Length <= 2 || !trustedIdentities.TryGetValue(segments[0] + "|" + segments[1], out string? hash)) continue;
                    string archiveName = segments[0] + "." + segments[1] + ".nupkg";
                    string? archivePath = Directory.EnumerateFiles(Path.Combine(outputRoot, "packages-source"), "*.nupkg")
                        .FirstOrDefault(candidate => Path.GetFileName(candidate).Equals(archiveName, StringComparison.OrdinalIgnoreCase));
                    if (archivePath is null) continue;
                    using VerifiedPackageArchive? archive = VerifiedPackageArchive.TryOpen(archivePath, hash);
                    if (archive?.VerifyExtractedFile(string.Join("/", segments.Skip(2)), path) == true) return true;
                }
                return false;
            }
            foreach (var pair in customDocuments)
            {
                XDocument document = new(pair.Value);
                foreach (XElement target in document.Descendants().Where(element => element.Name.LocalName == "Target").ToArray())
                    if (IsDefinitelyUninvokedControlledContextTarget(target, documents, toolchainNames, [globals, properties], requestedTargets)) target.Remove();
                XDocument[] related = customDocuments.Select(pair => pair.Value).ToArray();
                bool trustedPackage = IsAdmittedTrustedPackageInput(pair.Key);
                if (!trustedPackage && !HasOnlyControlledDocumentTaskFileInputs(document, pair.Key,
                        workingDirectory, outputRoot, outputRoot,
                        customDocuments.Select(pair => (pair.Value, pair.Key)).ToArray(),
                        properties, Path.GetFullPath(invocation[1]),
                        immutableGlobalProperties: immutableGlobals))
                {
                    failureReason = $"controlled context contains an uncontrolled task file input in '{pair.Key}'.";
                    return false;
                }
                if (!trustedPackage && ContainsUncontrolledControlledBuildTask(document, related, properties, immutableGlobals, conservativeSdkHooks: true))
                {
                    failureReason = $"controlled context contains an uncontrolled executable task in '{pair.Key}'.";
                    return false;
                }
                foreach (XElement assignment in document.Descendants().Where(element =>
                             properties.TryGetValue(matchedName, out string? matched) && matched == "true" &&
                             element.Ancestors().Any(parent => parent.Name.LocalName == "Target")))
                {
                    string? name = assignment.Parent?.Name.LocalName == "PropertyGroup" ? assignment.Name.LocalName
                        : assignment.Name.LocalName == "Output" ? assignment.Attribute("PropertyName")?.Value : null;
                    if (name is not null) name = DecodeMsBuildEscapes(name);
                    if (name is null || (!ContainsUnresolvedBuildExpression(name) && !ControlledContextPathProperties.Contains(name)) ||
                        IsDefinitelyInactiveControlledBuildOperation(assignment, properties, definingProjectPath: null, immutableGlobalProperties: immutableGlobals)) continue;
                    failureReason = $"target-time assignment to an isolation-sensitive property or dynamic property '{name}' in '{pair.Key}'.";
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"{exception.GetType().Name} while inspecting the controlled MSBuild context: {exception.Message}";
            return false;
        }
        finally { TryDeleteFile(preprocessPath); TryDeleteFile(responsePath); }
    }
}
