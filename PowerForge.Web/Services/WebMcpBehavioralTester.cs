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
                  const searchSurface = Array.from(document.querySelectorAll('[data-webmcp-site-search]'))
                    .find(surface => surface.getAttribute('data-webmcp-tool-name') === request.toolName);
                  if (!searchSurface) throw new Error('No search surface declares the requested WebMCP tool.');
                  const runtime = window.PowerForgeWebMcpSearch;
                  const ownsRuntime = runtime?.registeredToolName === request.toolName;
                  const readBinding = () => ownsRuntime ? runtime.visibleElements : null;
                  let binding = readBinding();
                  const initialBoundResults = binding?.results;
                  const hasExternalRenderer = ownsRuntime && typeof runtime.renderVisibleResults === 'function';
                  const isBoxVisible = element => element.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true });
                  const isTextVisible = node => {
                    let parent = node.parentElement;
                    while (parent && getComputedStyle(parent).display === 'contents') {
                      const style = getComputedStyle(parent);
                      if (style.visibility !== 'visible' || Number(style.opacity) === 0) return false;
                      parent = parent.parentElement;
                    }
                    if (!parent || !isBoxVisible(parent)) return false;
                    const range = document.createRange();
                    range.selectNodeContents(node);
                    return Array.from(range.getClientRects()).some(rect => rect.width > 0 && rect.height > 0);
                  };
                  const isVisible = element => {
                    if (isBoxVisible(element)) return true;
                    if (getComputedStyle(element).display !== 'contents') return false;
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    let node;
                    while ((node = walker.nextNode())) {
                      if (String(node.nodeValue || '').trim() && isTextVisible(node)) return true;
                    }
                    return Array.from(element.querySelectorAll('*')).some(isBoxVisible);
                  };
                  const candidates = (selector, boundElement) => Array.from(new Set([
                    ...document.querySelectorAll(selector), ...(boundElement ? [boundElement] : [])
                  ])).filter(element => element?.nodeType === 1 && element.ownerDocument === document)
                    .filter(element => {
                      const owner = element.closest('[data-webmcp-site-search]');
                      return (owner === searchSurface || owner === null) && isVisible(element);
                    });
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
                    .filter(isVisible)
                    .map(anchor => normalizeUrl(anchor.getAttribute('href')))
                    .filter(Boolean);
                  const readVisibleText = region => {
                    if (!region) return '';
                    const walker = document.createTreeWalker(region, NodeFilter.SHOW_TEXT);
                    const parts = [];
                    let length = 0;
                    let node;
                    while (length < 1000 && (node = walker.nextNode())) {
                      if (!isTextVisible(node)) continue;
                      const text = String(node.nodeValue || '').trim().slice(0, 1000 - length);
                      if (text) {
                        parts.push(text);
                        length += text.length + 1;
                      }
                    }
                    return parts.join(' ').slice(0, 1000);
                  };
                  const initialInputs = new Map(candidates('[data-search-page-input], #pf-search-query', binding?.input)
                    .map(element => [element, element.value]));
                  const initialResults = new Map(candidates(visibleResultSelector, binding?.results).map(region => [region, {
                    text: readVisibleText(region), urls: JSON.stringify(readVisibleUrls(region))
                  }]));
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
                  binding = readBinding();
                  if (binding && [binding.input, binding.results].some(element =>
                      element?.ownerDocument !== document || element?.nodeType !== 1 || !element.isConnected ||
                      (element.closest('[data-webmcp-site-search]') &&
                       element.closest('[data-webmcp-site-search]') !== searchSurface))) {
                    throw new Error('The search tool visibleElements binding must identify connected elements owned by this tool.');
                  }
                  const inputs = candidates('[data-search-page-input], #pf-search-query', binding?.input);
                  const externalInputs = inputs.filter(element => !searchSurface.contains(element) &&
                    element.value === request.query && element.value !== initialInputs.get(element));
                  const regions = new Set(candidates(visibleResultSelector, binding?.results));
                  // An emptied boxless region has no visible descendants, but its disappearance
                  // still provides the same zero-results transition as an emptied boxed region.
                  if (output?.totalMatches === 0) {
                    initialResults.forEach((_, region) => regions.add(region));
                    if (binding) regions.add(binding.results);
                  }
                  const observations = Array.from(regions).map(region => {
                    const text = readVisibleText(region);
                    const urls = readVisibleUrls(region);
                    const initial = initialResults.get(region);
                    return { region, text, urls, changed: text !== (initial?.text || '') ||
                      JSON.stringify(urls) !== (initial?.urls || '[]') };
                  });
                  const scopedResults = observations.filter(item => searchSurface.contains(item.region));
                  // Explicit bindings own external results. Legacy renderers can identify an
                  // external UI only when there is no competing scoped result region.
                  const externalResults = hasExternalRenderer && scopedResults.length === 0
                    ? observations.filter(item => !searchSurface.contains(item.region) &&
                      (item.changed || externalInputs.length > 0)) : [];
                  const outputResultUrls = Array.from(output?.results || [])
                    .map(result => normalizeUrl(result?.url)).filter(Boolean);
                  const matchesOutput = item => outputResultUrls.every(url => item.urls.includes(url));
                  const observation = binding
                    ? observations.find(item => item.region === binding.results)
                    : [...scopedResults, ...externalResults].find(item => item.changed && matchesOutput(item)) ||
                      scopedResults.find(matchesOutput) || externalResults.find(matchesOutput) ||
                      scopedResults[0] || externalResults[0];
                  if (binding && observation && output?.totalMatches === 0 && initialBoundResults !== binding.results) {
                    const previous = observations.find(item => item.region === initialBoundResults);
                    observation.changed ||= previous?.changed === true;
                  }
                  const scopedInputs = inputs.filter(element => searchSurface.contains(element));
                  const visibleInput = binding ? inputs.find(element => element === binding.input)
                    : scopedInputs.find(element => element.value === request.query) ||
                      (externalResults.includes(observation) ? externalInputs[0] ||
                        inputs.find(element => element.value === request.query) : null) || scopedInputs[0];
                  const visibleResultText = observation?.text || '';
                  const visibleResultUrls = observation?.urls || [];
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
                    visibleResultChanged: observation?.changed === true,
                    outputResultUrls,
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
