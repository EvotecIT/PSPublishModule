(function (global, document) {
  'use strict';

  var surface = document.querySelector('[data-webmcp-site-search]');
  if (!surface) return;

  var toolName = String(surface.getAttribute('data-webmcp-tool-name') || '').trim();
  var toolDescription = String(surface.getAttribute('data-webmcp-tool-description') || 'Search this website.').trim();
  var indexPath = String(surface.getAttribute('data-webmcp-search-index') || surface.getAttribute('data-search-index') || '/search/index.json').trim();
  if (!/^[A-Za-z0-9_-]{1,128}$/.test(toolName)) return;

  var api = global.PowerForgeWebMcpSearch || {};
  var adapter = api.adapter || null;
  var registrationController = null;

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
    return text.replace(/[^a-z0-9]+/g, ' ').trim();
  }

  function toArray(value) {
    if (Array.isArray(value)) return value;
    if (typeof value === 'string') return value.split(',');
    return [];
  }

  function shapeResult(item) {
    return {
      title: boundedText(item && item.title, 240),
      url: boundedText(item && item.url, 1000),
      description: boundedText(item && (item.description || item.snippet), 500),
      collection: boundedText(item && item.collection, 100),
      language: boundedText(item && item.language, 20),
      tags: toArray(item && item.tags).slice(0, 8).map(function (tag) { return boundedText(tag, 80); })
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
      var url = boundedText(item.url, 1000);
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
      results: matches.slice(0, limit).map(function (match) { return shapeResult(match.item); })
    };
  }

  function syncVisibleSearch(query) {
    var input = surface.querySelector('[data-search-page-input], #pf-search-query');
    if (!input) return;
    input.value = query;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  async function genericSearch(request) {
    var indexUrl = new URL(indexPath || '/search/index.json', document.baseURI);
    if (indexUrl.origin !== global.location.origin) {
      throw new Error('The WebMCP search index must be same-origin.');
    }

    var response = await global.fetch(indexUrl.href, {
      cache: 'no-cache',
      credentials: 'same-origin',
      signal: request.signal
    });
    if (!response.ok) throw new Error('Search index request failed with HTTP ' + response.status + '.');
    var entries = await response.json();
    var result = searchEntries(entries, request.query, request.limit);
    syncVisibleSearch(request.query);
    return result;
  }

  function normalizeResponse(result, request) {
    result = result || {};
    var source = Array.isArray(result.results) ? result.results : [];
    var results = source.slice(0, request.limit).map(shapeResult);
    var totalMatches = Number.isInteger(result.totalMatches) && result.totalMatches >= results.length
      ? result.totalMatches
      : results.length;
    return {
      query: request.query,
      totalMatches: totalMatches,
      returned: results.length,
      results: results
    };
  }

  async function execute(input, context) {
    input = input || {};
    var query = boundedText(input.query, 200).trim();
    if (!query) throw new TypeError('query must contain between 1 and 200 characters.');
    var request = {
      query: query,
      limit: boundedInteger(input.limit, 10, 1, 20),
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
            maximum: 20,
            default: 10,
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
