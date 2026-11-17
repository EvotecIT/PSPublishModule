using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using ConsoleMonster;
using ConsoleMonster.Capabilities;
using PowerForge;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

/// <summary>Hosts documentation rendering in a short-lived process when Windows PowerShell cannot isolate dependencies.</summary>
public static class DocumentationRenderProcess
{
    /// <summary>Renders finalized documentation from a private JSON request written by the documentation cmdlets.</summary>
    /// <param name="requestPath">Request file containing documents, module metadata and rendering options only.</param>
    /// <remarks>The embedded native worker calls this entry after registering the module's scoped resolver.
    /// Console output goes to standard output; HTML paths, logs and failures go to a sibling response file.
    /// No repository access, authentication or browser opening is performed by this entry point.</remarks>
    public static void Run(string requestPath)
    {
        var response = new DocumentationRenderResponse();
        try
        {
            var request = JsonSerializer.Deserialize(File.ReadAllText(requestPath),
                DocumentationRenderJsonContext.Default.DocumentationRenderRequest)
                ?? throw new InvalidDataException("The documentation render request is empty.");
            if (request.Module == null)
            {
                // Output is redirected to the parent, so detection would see a pipe; use the parent's console instead.
                Terminal.Profile = TerminalProfile.Fixed(Math.Max(1, request.ConsoleWidth), Math.Max(1, request.ConsoleHeight),
                    request.ColorDepth, request.Glyphs, OutputMode.Stream);
                RenderConsoleDirect(request.Items, request.JsonPreference, request.DefaultLanguage);
            }
            else
            {
                response.HtmlPath = ExportHtmlDirect(request.Module, request.Items, request.DestinationPath,
                    response.Logs.Add, request.CommandHelp);
            }
        }
        catch (Exception ex)
        {
            response.Error = ex.ToString();
            throw;
        }
        finally
        {
            File.WriteAllText(requestPath + ".response.json", JsonSerializer.Serialize(response,
                DocumentationRenderJsonContext.Default.DocumentationRenderResponse), new UTF8Encoding(false));
        }
    }

    internal static void RenderConsole(DocumentItem[] items, bool raw, JsonRendererPreference preference,
        string? language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (raw)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = GetModuleDocumentationCommand.ShouldRenderAsLocalFile(item)
                    ? File.ReadAllText(item.Path!) : item.Content ?? string.Empty;
                // Raw Markdown goes to standard output like the rendered worker output, so it can be captured.
                ConsoleOutput.WriteOutside(() => Console.WriteLine(text));
            }
            return;
        }
#if NETFRAMEWORK
        var profile = ConsoleOutput.CurrentProfile();
        var response = RunWorker(new DocumentationRenderRequest
        {
            Items = items,
            JsonPreference = preference,
            DefaultLanguage = language,
            ConsoleWidth = profile.Width,
            ConsoleHeight = profile.Height,
            ColorDepth = profile.ColorDepth,
            Glyphs = profile.Glyphs
        }, cancellationToken, null);
        // The worker's output is already encoded for this console.
        ConsoleOutput.WriteOutside(() => Console.Write(response.Output));
#else
        RenderConsoleDirect(items, preference, language);
#endif
    }

    internal static string ExportHtml(ModuleInfoModel module, DocumentItem[] items, string? destination,
        Action<string> log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commandHelp = new DocumentationCommandHelpCollector().Collect(module, log, cancellationToken);
#if NETFRAMEWORK
        var result = RunWorker(new DocumentationRenderRequest
        {
            Items = items,
            Module = module,
            CommandHelp = commandHelp,
            DestinationPath = destination == null ? null : Path.GetFullPath(destination)
        }, cancellationToken, log);
        return result.HtmlPath ?? throw new InvalidDataException("The documentation worker returned no HTML path.");
#else
        return ExportHtmlDirect(module, items, destination, log, commandHelp);
#endif
    }

    // Keep OfficeIMO/HtmlForgeX references out of the parent-facing methods' JIT bodies.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RenderConsoleDirect(DocumentItem[] items, JsonRendererPreference preference, string? language)
    {
        var renderer = new Renderer(preference, language);
        foreach (var item in items)
        {
            if (GetModuleDocumentationCommand.ShouldRenderAsLocalFile(item))
                renderer.ShowFile(item.Title, item.Path!, false);
            else
                renderer.ShowContent(item.Title, item.Content, false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ExportHtmlDirect(ModuleInfoModel module, DocumentItem[] items, string? destination, Action<string> log,
        IReadOnlyList<DocumentationCommandHelpItem> commandHelp)
        => new HtmlExporter().Export(module, items, destination, open: false, log: log, commandHelp: commandHelp);

#if NETFRAMEWORK
    private static DocumentationRenderResponse RunWorker(
        DocumentationRenderRequest request, CancellationToken cancellationToken, Action<string>? log)
    {
        var root = Path.Combine(Path.GetTempPath(), "PSPublishModule.Documentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestPath = Path.Combine(root, "request.json");
            var scriptPath = Path.Combine(root, "render.ps1");
            var loaderRoot = Path.Combine(root, "Loader");
            ModuleLoaderAssets.Write(loaderRoot);
            using (var script = typeof(DocumentationRenderProcess).Assembly.GetManifestResourceStream(
                "PSPublishModule.DocumentationRenderWorker.ps1")
                ?? throw new InvalidOperationException("The documentation worker resource is missing."))
            using (var target = File.Create(scriptPath))
                script.CopyTo(target);
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request,
                DocumentationRenderJsonContext.Default.DocumentationRenderRequest), new UTF8Encoding(false));

            var runRequest = new PowerShellRunRequest(scriptPath, new[]
            {
                "-AssemblyPath", typeof(DocumentationRenderProcess).Assembly.Location,
                "-LoaderPath", Path.Combine(loaderRoot, "Desktop", ModuleLoaderAssets.FileName),
                "-RequestPath", requestPath
            }, TimeSpan.FromMinutes(10), preferPwsh: false,
                hostRequirement: PowerShellHostRequirement.WindowsPowerShell,
                workingDirectory: Environment.CurrentDirectory, environmentVariables: WorkerEnvironment());
            var runner = (ICancellablePowerShellRunner)new PowerShellRunner(new ProcessRunner(ownProcessTree: true));
            var result = runner.RunAsync(runRequest, cancellationToken).GetAwaiter().GetResult();
            var responsePath = requestPath + ".response.json";
            var response = File.Exists(responsePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(responsePath),
                    DocumentationRenderJsonContext.Default.DocumentationRenderResponse)
                : null;
            if (response != null)
                foreach (var line in response.Logs) log?.Invoke(line);
            if (result.ExitCode != 0 || response == null || response.Error != null)
            {
                throw new InvalidOperationException($"Documentation rendering failed (exit code {result.ExitCode}). " +
                    (response?.Error ?? "The worker did not return a successful response.") + Environment.NewLine + result.StdErr);
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                throw new InvalidOperationException("Documentation rendering reported an error. " + result.StdErr);
            response.Output = result.StdOut;
            return response;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyDictionary<string, string?> WorkerEnvironment()
    {
        // The runner applies these overrides to its child. Do not inherit repository tokens or profile configuration.
        var allowed = new HashSet<string>(new[]
        {
            "SystemRoot", "WINDIR", "SystemDrive", "TEMP", "TMP", "PATH", "PATHEXT", "COMSPEC",
            "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "LOCALAPPDATA", "APPDATA", "ALLUSERSPROFILE", "ProgramData", "PROCESSOR_ARCHITECTURE"
        }, StringComparer.OrdinalIgnoreCase);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (!allowed.Contains(name)) environment[name] = null;
        }
        // Preserve module lookup for the exporter's existing command-help runspaces.
        environment["PSModulePath"] = Environment.GetEnvironmentVariable("PSModulePath");
        return environment;
    }
#endif
}
