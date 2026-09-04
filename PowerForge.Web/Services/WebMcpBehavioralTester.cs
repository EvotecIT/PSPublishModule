using System.Text.Json;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Exercises a rendered Website Tool through the canonical HtmlTinkerX browser host.</summary>
public static class WebMcpBehavioralTester
{
    internal static string RegistrationCaptureScript =>
        """
        (() => {
          const tools = Object.create(null);
          Object.defineProperty(window, '__powerForgeCapturedWebMcpTools', { value: tools, configurable: true });
          Object.defineProperty(document, 'modelContext', {
            configurable: true,
            value: {
              registerTool: async (tool, options) => {
                const signal = options?.signal;
                if (signal?.aborted) return;
                tools[tool.name] = tool;
                signal?.addEventListener('abort', () => {
                  if (tools[tool.name] === tool) delete tools[tool.name];
                }, { once: true });
              }
            }
          });
        })();
        """;

    /// <summary>Loads a page, captures imperative WebMCP registration, executes a tool, and checks visible synchronization.</summary>
    public static async Task<WebMcpBehavioralTestResult> TestSiteSearchAsync(
        WebMcpBehavioralTestOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var pageUri) ||
            pageUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A valid absolute HTTP or HTTPS Url is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ToolName))
            throw new ArgumentException("ToolName is required.", nameof(options));
        var query = options.Query?.Trim() ?? string.Empty;
        if (query.Length is < 1 or > 200)
            throw new ArgumentException("Query must contain between 1 and 200 characters.", nameof(options));
        if (options.Limit is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(options), "Limit must be zero or between 1 and 5.");

        if (options.EnsureBrowserInstalled)
            await HtmlBrowser.EnsureInstalledAsync(HtmlBrowserEngine.Chromium).ConfigureAwait(false);

        var launch = new HtmlBrowserLaunchOptions
        {
            Browser = HtmlBrowserEngine.Chromium,
            Headless = options.Headless,
            Timeout = options.TimeoutMs <= 0 ? 30_000 : options.TimeoutMs,
            LoadState = HtmlBrowserLoadState.NetworkIdle,
            ViewportWidth = 1440,
            ViewportHeight = 1000
        };
        launch.InitScripts.Add(RegistrationCaptureScript);

        try
        {
            await using var session = await HtmlBrowser.OpenSessionAsync(options.Url, launch, cancellationToken).ConfigureAwait(false);
            var timeout = launch.Timeout;
            await session.Page.WaitForFunctionAsync(
                "name => Boolean(window.__powerForgeCapturedWebMcpTools && window.__powerForgeCapturedWebMcpTools[name])",
                options.ToolName,
                new() { Timeout = timeout }).ConfigureAwait(false);

            var requestJson = JsonSerializer.Serialize(new
            {
                toolName = options.ToolName,
                query,
                limit = options.Limit,
                timeoutMs = timeout
            }, WebJson.Options);
            var observationTask = session.Page.EvaluateAsync<string>(
                $$"""
                async () => {
                  const request = {{requestJson}};
                  const tools = window.__powerForgeCapturedWebMcpTools || {};
                  const tool = tools[request.toolName];
                  const input = request.limit > 0 ? { query: request.query, limit: request.limit } : { query: request.query };
                  const visibleResultSelector = '[data-search-page-results], [data-search-results], #pf-search-results, .search-results, .ev-search-page-results';
                  const normalizeUrl = (value) => {
                    try {
                      const resolved = new URL(value, document.baseURI);
                      resolved.hash = '';
                      return resolved.href;
                    } catch {
                      return '';
                    }
                  };
                  const readVisibleUrls = (region) => Array.from(region?.querySelectorAll('a[href]') || [])
                    .map(anchor => normalizeUrl(anchor.getAttribute('href')))
                    .filter(Boolean);
                  const initialVisibleResults = document.querySelector(visibleResultSelector);
                  const initialVisibleResultText = String(initialVisibleResults?.innerText || '').slice(0, 1000);
                  const initialVisibleResultUrls = readVisibleUrls(initialVisibleResults);
                  const controller = new AbortController();
                  const timeoutId = setTimeout(() => controller.abort(), request.timeoutMs);
                  let output;
                  try {
                    output = await Promise.race([
                      tool.execute(input, { signal: controller.signal }),
                      new Promise((_, reject) => controller.signal.addEventListener('abort', () => {
                        reject(new DOMException('The WebMCP tool execution timed out.', 'TimeoutError'));
                      }, { once: true }))
                    ]);
                  } finally {
                    clearTimeout(timeoutId);
                  }
                  await new Promise(resolve => setTimeout(resolve, 300));
                  const visibleInput = document.querySelector('[data-search-page-input], #pf-search-query');
                  const visibleResults = document.querySelector(visibleResultSelector);
                  const visibleResultText = String(visibleResults?.innerText || '').slice(0, 1000);
                  const visibleResultUrls = readVisibleUrls(visibleResults);
                  return JSON.stringify({
                    registeredTools: Object.keys(tools).sort(),
                    schemaType: tool.inputSchema?.type,
                    schemaQueryType: tool.inputSchema?.properties?.query?.type,
                    schemaQueryMinimum: tool.inputSchema?.properties?.query?.minLength,
                    schemaQueryMaximum: tool.inputSchema?.properties?.query?.maxLength,
                    schemaLimitType: tool.inputSchema?.properties?.limit?.type,
                    schemaLimitMinimum: tool.inputSchema?.properties?.limit?.minimum,
                    schemaMaximum: tool.inputSchema?.properties?.limit?.maximum,
                    schemaDefault: tool.inputSchema?.properties?.limit?.default,
                    schemaRequired: Array.from(tool.inputSchema?.required || []).sort(),
                    schemaAdditionalProperties: tool.inputSchema?.additionalProperties,
                    readOnlyHint: tool.annotations?.readOnlyHint === true,
                    untrustedContentHint: tool.annotations?.untrustedContentHint === true,
                    visibleQuery: visibleInput?.value || '',
                    visibleResultText,
                    visibleResultUrls,
                    visibleResultChanged: visibleResultText !== initialVisibleResultText ||
                      JSON.stringify(visibleResultUrls) !== JSON.stringify(initialVisibleResultUrls),
                    outputResultUrls: Array.from(output?.results || [])
                      .map(result => normalizeUrl(result?.url))
                      .filter(Boolean),
                    output,
                    outputJson: JSON.stringify(output)
                  });
                }
                """);
            var observationJson = await observationTask
                .WaitAsync(TimeSpan.FromMilliseconds((long)timeout + 1_000), cancellationToken)
                .ConfigureAwait(false);

            return ParseObservation(options, query, observationJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WebMcpBehavioralTestResult
            {
                Success = false,
                Url = options.Url,
                ToolName = options.ToolName,
                Query = query,
                Errors = [$"Browser execution failed: {ex.Message}"]
            };
        }
    }

    internal static WebMcpBehavioralTestResult ParseObservation(
        WebMcpBehavioralTestOptions options,
        string query,
        string? observationJson)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(observationJson))
        {
            errors.Add("The browser returned no WebMCP observation.");
            return Failed(options, query, errors);
        }

        try
        {
            using var document = JsonDocument.Parse(observationJson);
            var root = document.RootElement;
            var registeredTools = root.GetProperty("registeredTools").EnumerateArray()
                .Select(static item => item.GetString() ?? string.Empty)
                .Where(static item => item.Length > 0)
                .ToArray();
            var output = root.GetProperty("output");
            var outputJson = root.GetProperty("outputJson").GetString() ?? string.Empty;
            var returned = output.GetProperty("returned").GetInt32();
            var resultCount = output.GetProperty("results").GetArrayLength();
            var totalMatches = output.GetProperty("totalMatches").GetInt32();
            var moreResultsAvailable = output.GetProperty("moreResultsAvailable").GetBoolean();
            var visibleQuery = root.GetProperty("visibleQuery").GetString() ?? string.Empty;
            var visibleResultText = root.GetProperty("visibleResultText").GetString() ?? string.Empty;
            var visibleResultUrls = ReadStringArray(root.GetProperty("visibleResultUrls"));
            var outputResultUrls = ReadStringArray(root.GetProperty("outputResultUrls"));
            var visibleResultChanged = root.GetProperty("visibleResultChanged").GetBoolean();

            if (!registeredTools.Contains(options.ToolName, StringComparer.Ordinal))
                errors.Add($"Expected tool '{options.ToolName}' was not registered.");
            var required = ReadStringArray(root.GetProperty("schemaRequired"));
            if (root.GetProperty("schemaType").GetString() != "object" ||
                root.GetProperty("schemaQueryType").GetString() != "string" ||
                root.GetProperty("schemaQueryMinimum").GetInt32() != 1 ||
                root.GetProperty("schemaQueryMaximum").GetInt32() != 200 ||
                root.GetProperty("schemaLimitType").GetString() != "integer" ||
                root.GetProperty("schemaLimitMinimum").GetInt32() != 1 ||
                root.GetProperty("schemaMaximum").GetInt32() != 5 ||
                root.GetProperty("schemaDefault").GetInt32() != 3 ||
                required.Length != 1 || required[0] != "query" ||
                root.GetProperty("schemaAdditionalProperties").GetBoolean())
                errors.Add("The registered input schema does not enforce the canonical query, limit, required-field, and additional-property contract.");
            if (!root.GetProperty("readOnlyHint").GetBoolean() || !root.GetProperty("untrustedContentHint").GetBoolean())
                errors.Add("The registered tool does not declare both read-only and untrusted-content annotations.");
            if (!string.Equals(output.GetProperty("query").GetString(), query, StringComparison.Ordinal))
                errors.Add("The tool response query does not match the requested query.");
            if (returned != resultCount || returned > 5)
                errors.Add("The tool returned an inconsistent or excessive result count.");
            var effectiveLimit = options.Limit > 0 ? options.Limit : 3;
            if (returned > effectiveLimit)
                errors.Add($"The tool returned {returned} results despite an effective limit of {effectiveLimit}.");
            if (totalMatches < returned)
                errors.Add("The tool reported fewer total matches than returned results.");
            if (moreResultsAvailable != (totalMatches > returned))
                errors.Add("The tool's more-results flag is inconsistent with its total and returned counts.");
            if (outputJson.Length > 1_500)
                errors.Add($"The serialized tool response is {outputJson.Length} characters; the limit is 1500.");
            if (!string.Equals(visibleQuery, query, StringComparison.Ordinal))
                errors.Add("The visible search input was not synchronized with the tool query.");
            if (returned > 0 && string.IsNullOrWhiteSpace(visibleResultText))
                errors.Add("The tool returned results but the visible results region remained empty.");
            if (returned > 0 && (outputResultUrls.Length != returned ||
                outputResultUrls.Any(url => !visibleResultUrls.Contains(url, StringComparer.Ordinal))))
                errors.Add("The visible results do not contain every URL returned by the tool invocation.");
            if (totalMatches == 0 && (visibleResultUrls.Length > 0 || !visibleResultChanged))
                errors.Add("The visible results did not transition to a verifiable zero-results state.");
            if (totalMatches > 0 && returned == 0 &&
                (!visibleResultChanged || string.IsNullOrWhiteSpace(visibleResultText)))
                errors.Add("The visible results did not expose the matches omitted from the bounded tool response.");

            return new WebMcpBehavioralTestResult
            {
                Success = errors.Count == 0,
                Url = options.Url,
                ToolName = options.ToolName,
                Query = query,
                RegisteredTools = registeredTools,
                TotalMatches = totalMatches,
                Returned = returned,
                OutputCharacters = outputJson.Length,
                MoreResultsAvailable = moreResultsAvailable,
                OutputTruncated = output.GetProperty("outputTruncated").GetBoolean(),
                VisibleQuery = visibleQuery,
                VisibleResultText = visibleResultText,
                VisibleResultUrls = visibleResultUrls,
                VisibleResultChanged = visibleResultChanged,
                OutputJson = outputJson,
                Errors = errors.ToArray()
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            errors.Add($"The browser observation was invalid: {ex.Message}");
            return Failed(options, query, errors);
        }
    }

    private static string[] ReadStringArray(JsonElement value) => value.EnumerateArray()
        .Select(static item => item.GetString() ?? string.Empty)
        .Where(static item => item.Length > 0)
        .ToArray();

    private static WebMcpBehavioralTestResult Failed(
        WebMcpBehavioralTestOptions options,
        string query,
        List<string> errors) => new()
    {
        Success = false,
        Url = options.Url,
        ToolName = options.ToolName,
        Query = query,
        Errors = errors.ToArray()
    };
}
