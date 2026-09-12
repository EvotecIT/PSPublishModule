using System.Text.Json;

namespace PowerForge.Tests;

// Artifact-shaped fixture for tests that substitute only the dotnet process boundary.
internal static class ReleaseValidationToolInstallFixture
{
    internal static void Complete(ProcessRunRequest request, string commandName, bool hiddenManifest = false)
    {
        var arguments = request.Arguments.ToArray();
        if (arguments.Length < 3 || arguments[0] != "tool" || arguments[1] != "install") { return; }
        var version = arguments[Array.IndexOf(arguments, "--version") + 1];
        var pathIndex = Array.IndexOf(arguments, "--tool-path");
        if (pathIndex >= 0) {
            Directory.CreateDirectory(arguments[pathIndex + 1]);
            File.WriteAllText(Path.Combine(arguments[pathIndex + 1], commandName + (OperatingSystem.IsWindows() ? ".exe" : "")), "installed shim");
        } else {
            var config = Directory.CreateDirectory(hiddenManifest ? Path.Combine(request.WorkingDirectory, ".config") : request.WorkingDirectory).FullName;
            File.WriteAllText(Path.Combine(config, "dotnet-tools.json"), JsonSerializer.Serialize(new {
                version = 1, isRoot = true, tools = new Dictionary<string, object> {
                    [arguments[2].ToLowerInvariant()] = new { version, commands = new[] { commandName } }
                }
            }));
        }
    }
}
