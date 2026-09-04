(function (global, document) {
  'use strict';

  var surface = document.querySelector('[data-webmcp-site-search]');
  if (!surface) return;

  var toolName = String(surface.getAttribute('data-webmcp-tool-name') || '').trim();
  var toolDescription = String(surface.getAttribute('data-webmcp-tool-description') || 'Search this website.').trim();
  var indexPath = String(surface.getAttribute('data-webmcp-search-index') || surface.getAttribute('data-search-index') || '/search/index.json').trim();
  if (!/^[A-Za-z0-9_-]{1,128}$/.test(toolName)) return;

  var DEFAULT_RESULT_LIMIT = 3;
  var MAX_RESULT_LIMIT = 5;
  var MAX_RESULT_URL_CHARACTERS = 400;
  var MAX_OUTPUT_CHARACTERS = 1500;
  var MAX_INDEX_BYTES = 8 * 1024 * 1024;
  var MAX_INDEX_ENTRIES = 5000;

  var api = global.PowerForgeWebMcpSearch || {};
  var adapter = api.adapter || null;
  var registrationController = null;
  var indexPromise = null;

  function boundedInteger(value, fallback, minimum, maximum) {
    var parsed = Number(value);
    if (!Number.isInteger(parsed)) return fallback;
    return Math.min(maximum, Math.max(minimum, parsed));
  }

  function boundedText(value, maximum) {
    return String(value == null ? '' : value).slice(0, maximum);
  }

  function normalizeText(value) {
    var text = boundedText(value, 20000).toLowerCase();
    if (typeof text.normalize === 'function') {
      text = text.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }
    return text.replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
  }

  function toArray(value) {
    if (Array.isArray(value)) return value;
    if (typeof value === 'string') return value.split(',');
    return [];
  }

  function shapeResult(item) {
    var url = String(item && item.url || '').trim();
    if (!url || url.length > MAX_RESULT_URL_CHARACTERS) return null;
    return {
      title: boundedText(item && item.title, 120),
      url: url,
      description: boundedText(item && (item.description || item.snippet), 200),
      collection: boundedText(item && item.collection, 48),
      language: boundedText(item && item.language, 12),
      tags: toArray(item && item.tags).slice(0, 4).map(function (tag) { return boundedText(tag, 32); })
    };
  }

  function searchEntries(entries, query, limit) {
    var normalizedQuery = normalizeText(query);
    var queryTokens = normalizedQuery.split(' ').filter(Boolean);
    var matches = [];
    var seen = Object.create(null);

    if (!normalizedQuery || !Array.isArray(entries)) {
      return { totalMatches: 0, results: [] };
    }

    entries.forEach(function (item) {
      item = item || {};
      var url = String(item.url || '').trim();
      if (!url || seen[url]) return;

      var title = normalizeText(item.title);
      var haystack = normalizeText([
        item.title,
        item.description,
        item.snippet,
        item.searchText,
        item.collection,
        item.kind,
        toArray(item.tags).join(' '),
        toArray(item.categories).join(' ')
      ].join(' '));
      if (!haystack) return;

      var score = 0;
      if (title === normalizedQuery) score += 120;
      else if (title.indexOf(normalizedQuery) === 0) score += 80;
      else if (title.indexOf(normalizedQuery) >= 0) score += 50;
      if (haystack.indexOf(normalizedQuery) >= 0) score += 30;
      queryTokens.forEach(function (token) {
        if (haystack.indexOf(token) >= 0) score += 8;
      });
      if (score <= 0) return;

      seen[url] = true;
      matches.push({ item: item, score: score, weight: Number(item.weight || 0) });
    });

    matches.sort(function (left, right) {
      return right.score - left.score || right.weight - left.weight ||
        String(left.item.title || '').localeCompare(String(right.item.title || ''));
    });

    return {
      totalMatches: matches.length,
      results: matches.slice(0, limit).map(function (match) { return match.item; })
    };
  }

  function syncVisibleSearch(query) {
    var input = surface.querySelector('[data-search-page-input], #pf-search-query');
    if (!input) return;
    input.value = query;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function loadIndex() {
    var indexUrl = new URL(indexPath || '/search/index.json', document.baseURI);
    if (indexUrl.origin !== global.location.origin) {
      throw new Error('The WebMCP search index must be same-origin.');
    }

    if (!indexPromise) {
      indexPromise = global.fetch(indexUrl.href, {
        cache: 'no-cache',
        credentials: 'same-origin'
      }).then(function (response) {
        if (!response.ok) throw new Error('Search index request failed with HTTP ' + response.status + '.');
        var lengthHeader = response.headers && response.headers.get('content-length');
        var advertisedLength = lengthHeader == null ? NaN : Number(lengthHeader);
        var contentEncoding = String(response.headers && response.headers.get('content-encoding') || '').trim();
        if (!contentEncoding && Number.isFinite(advertisedLength) && advertisedLength > MAX_INDEX_BYTES) {
          throw new Error('Search index exceeds the ' + MAX_INDEX_BYTES + '-byte safety limit.');
        }
        return readBoundedResponseText(response);
      }).then(function (json) {
        var entries = JSON.parse(json);
        if (!Array.isArray(entries)) throw new Error('Search index response must be a JSON array.');
        if (entries.length > MAX_INDEX_ENTRIES) {
          throw new Error('Search index exceeds the ' + MAX_INDEX_ENTRIES + '-entry safety limit.');
        }
        return entries;
      }).catch(function (error) {
        indexPromise = null;
        throw error;
      });
    }

    return indexPromise;
  }

  async function readBoundedResponseText(response) {
    if (response.body && typeof response.body.getReader === 'function') {
      var reader = response.body.getReader();
      var chunks = [];
      var total = 0;
      try {
        while (true) {
          var next = await reader.read();
          if (next.done) break;
          total += next.value.byteLength;
          if (total > MAX_INDEX_BYTES) {
            await reader.cancel('Search index safety limit exceeded.');
            throw new Error('Search index exceeds the ' + MAX_INDEX_BYTES + '-byte safety limit.');
          }
          chunks.push(next.value);
        }
      } finally {
        reader.releaseLock();
      }

      var bytes = new Uint8Array(total);
      var offset = 0;
      chunks.forEach(function (chunk) {
        bytes.set(chunk, offset);
        offset += chunk.byteLength;
      });
      return new TextDecoder('utf-8').decode(bytes);
    }

    var fallbackBytes = new Uint8Array(await response.arrayBuffer());
    if (fallbackBytes.byteLength > MAX_INDEX_BYTES) {
      throw new Error('Search index exceeds the ' + MAX_INDEX_BYTES + '-byte safety limit.');
    }
    return new TextDecoder('utf-8').decode(fallbackBytes);
  }

  function awaitWithSignal(promise, signal) {
    if (!signal) return promise;
    if (signal.aborted) return Promise.reject(new DOMException('The WebMCP search was cancelled.', 'AbortError'));

    return new Promise(function (resolve, reject) {
      function cleanup() {
        signal.removeEventListener('abort', abort);
      }
      function abort() {
        cleanup();
        reject(new DOMException('The WebMCP search was cancelled.', 'AbortError'));
      }
      signal.addEventListener('abort', abort, { once: true });
      promise.then(function (value) {
        cleanup();
        resolve(value);
      }, function (error) {
        cleanup();
        reject(error);
      });
    });
  }

  async function genericSearch(request) {
    var entries = await awaitWithSignal(loadIndex(), request.signal);
    var result = searchEntries(entries, request.query, request.limit);
    syncVisibleSearch(request.query);
    return result;
  }

  function normalizeResponse(result, request) {
    result = result || {};
    var source = Array.isArray(result.results) ? result.results : [];
    var selected = source.slice(0, request.limit);
    var shaped = selected.map(shapeResult).filter(Boolean);
    var totalMatches = Number.isInteger(result.totalMatches) && result.totalMatches >= source.length
      ? result.totalMatches
      : source.length;
    var response = {
      query: request.query,
      totalMatches: totalMatches,
      returned: 0,
      moreResultsAvailable: totalMatches > 0,
      outputTruncated: source.length > selected.length || shaped.length !== selected.length,
      results: []
    };

    shaped.forEach(function (item) {
      response.results.push(item);
      response.returned = response.results.length;
      response.moreResultsAvailable = totalMatches > response.returned;
      if (JSON.stringify(response).length > MAX_OUTPUT_CHARACTERS) {
        response.results.pop();
        response.returned = response.results.length;
        response.moreResultsAvailable = totalMatches > response.returned;
        response.outputTruncated = true;
      }
    });
    if (shaped.length > response.returned) response.outputTruncated = true;
    return response;
  }

  async function execute(input, context) {
    input = input || {};
    var query = boundedText(input.query, 200).trim();
    if (!query) throw new TypeError('query must contain between 1 and 200 characters.');
    var request = {
      query: query,
      limit: boundedInteger(input.limit, DEFAULT_RESULT_LIMIT, 1, MAX_RESULT_LIMIT),
      signal: context && context.signal
    };
    var result = adapter && typeof adapter.search === 'function'
      ? await adapter.search(request)
      : await genericSearch(request);
    return normalizeResponse(result, request);
  }

  api.bindAdapter = function (nextAdapter) {
    if (!nextAdapter || typeof nextAdapter.search !== 'function') {
      throw new TypeError('A WebMCP site-search adapter must provide search(request).');
    }
    adapter = nextAdapter;
    api.adapter = nextAdapter;
  };
  api.invalidateIndex = function () {
    indexPromise = null;
    if (adapter && typeof adapter.invalidateIndex === 'function') adapter.invalidateIndex();
  };
  api.normalizeText = normalizeText;
  api.searchEntries = searchEntries;
  api.dispose = function () {
    if (registrationController) registrationController.abort();
    registrationController = null;
    surface.setAttribute('data-webmcp-status', 'disposed');
  };
  global.PowerForgeWebMcpSearch = api;

  async function register() {
    if (!document.modelContext || typeof document.modelContext.registerTool !== 'function') {
      surface.setAttribute('data-webmcp-status', 'unsupported');
      return false;
    }
    if (api.registeredToolName === toolName) return true;

    registrationController = new AbortController();
    await document.modelContext.registerTool({
      name: toolName,
      description: toolDescription,
      inputSchema: {
        type: 'object',
        properties: {
          query: {
            type: 'string',
            minLength: 1,
            maxLength: 200,
            description: 'Search query.'
          },
          limit: {
            type: 'integer',
            minimum: 1,
            maximum: MAX_RESULT_LIMIT,
            default: DEFAULT_RESULT_LIMIT,
            description: 'Maximum number of results to return.'
          }
        },
        required: ['query'],
        additionalProperties: false
      },
      annotations: {
        readOnlyHint: true,
        untrustedContentHint: true
      },
      execute: execute
    }, { signal: registrationController.signal });
    api.registeredToolName = toolName;
    surface.setAttribute('data-webmcp-status', 'registered');
    return true;
  }

  api.ready = document.readyState === 'loading'
    ? new Promise(function (resolve) {
        document.addEventListener('DOMContentLoaded', function () {
          register().then(resolve, function () {
            surface.setAttribute('data-webmcp-status', 'failed');
            resolve(false);
          });
        }, { once: true });
      })
    : register().catch(function () {
        surface.setAttribute('data-webmcp-status', 'failed');
        return false;
      });
})(window, document);
