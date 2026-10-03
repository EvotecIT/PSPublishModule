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
  var MAX_RESULT_URL_CHARACTERS = 1024;
  var MAX_OUTPUT_CHARACTERS = 1500;
  var MAX_INDEX_BYTES = 8 * 1024 * 1024;
  var MAX_INDEX_ENTRIES = 5000;

  var api = global.PowerForgeWebMcpSearch || {};
  var adapter = api.adapter || null;
  var renderVisibleResults = typeof api.renderVisibleResults === 'function'
    ? api.renderVisibleResults
    : null;
  var registrationController = null;
  var indexPromise = null;
  var manifestPromise = null;
  var shardPromises = Object.create(null);
  var cachedShardBytes = 0;
  var searchFields = new WeakMap();

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
      text = text.normalize('NFD').replace(/\p{Mn}/gu, '');
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
      kind: boundedText(item && item.kind, 48),
      project: boundedText(item && item.project, 160),
      meta: item && item.collection === 'api' ? {
        signature: boundedText(item.meta && item.meta.signature, 300),
        namespace: boundedText(item.meta && item.meta.namespace, 160),
        receiverType: boundedText(item.meta && item.meta.receiverType, 160)
      } : undefined,
      language: boundedText(item && item.language, 12),
      tags: toArray(item && item.tags).slice(0, 4).map(function (tag) { return boundedText(tag, 32); })
    };
  }

  function searchEntries(entries, query, limit, filters) {
    var normalizedQuery = normalizeText(query);
    var queryTokens = normalizedQuery.split(' ').filter(Boolean);
    var matches = [];
    var seen = Object.create(null);

    if (!normalizedQuery || !Array.isArray(entries)) {
      return { totalMatches: 0, results: [] };
    }

    entries.forEach(function (item) {
      item = item || {};
      if (filters && filters.project && item.project !== filters.project) return;
      if (filters && filters.kind && item.kind !== filters.kind) return;
      var url = String(item.url || '').trim();
      if (!url || seen[url]) return;

      var fields = searchFields.get(item);
      if (!fields) {
        var aliasFields = toArray(item.aliases).map(normalizeText);
        var searchText = [
        item.title,
        item.description,
        item.snippet,
        item.searchText,
        item.collection,
        item.kind,
        toArray(item.aliases).join(' '),
        toArray(item.tags).join(' '),
        toArray(item.categories).join(' ')
        ].join(' ');
        fields = { title: normalizeText(item.title), aliases: aliasFields,
          haystack: normalizeText(searchText) + ' ' + normalizeText(searchText.replace(/([a-z])([A-Z])/g, '$1 $2')) };
        searchFields.set(item, fields);
      }
      var title = fields.title;
      var aliases = fields.aliases;
      var haystack = fields.haystack;
      if (!haystack) return;
      if (!queryTokens.every(function (token) { return haystack.indexOf(token) >= 0; })) return;

      var score = 0;
      if (title === normalizedQuery) score += 120;
      else if (aliases.indexOf(normalizedQuery) >= 0) score += 110;
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

  function throwIfAborted(signal) {
    if (signal && signal.aborted) {
      throw new DOMException('The WebMCP search was cancelled.', 'AbortError');
    }
  }

  async function syncVisibleSearch(response, request, usesAdapter) {
    throwIfAborted(request.signal);

    if (renderVisibleResults) {
      var visibleResponse = JSON.parse(JSON.stringify(response));
      await awaitWithSignal(Promise.resolve().then(function () {
        throwIfAborted(request.signal);
        return renderVisibleResults(visibleResponse, { signal: request.signal });
      }), request.signal);
      return;
    }

    if (usesAdapter) return;
    var input = surface.querySelector('[data-search-page-input], #pf-search-query');
    if (input) {
      input.value = response.query;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
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
    var entries = await awaitWithSignal(loadQueryEntries(request), request.signal);
    return searchEntries(entries, request.query, request.limit, request);
  }

  async function loadQueryEntries(request) {
    var indexUrl = new URL(indexPath, document.baseURI);
    if (indexUrl.origin !== global.location.origin) throw new Error('Search indexes must be same-origin.');
    var manifestUrl = new URL('manifest.json', indexUrl);
    if (!manifestPromise) {
      manifestPromise = global.fetch(manifestUrl.href, { credentials: 'same-origin', cache: 'no-cache' })
        .then(async function (response) {
          if (response.status === 404) return null;
          if (!response.ok) throw new Error('Search manifest request failed with HTTP ' + response.status + '.');
          return JSON.parse(await readBoundedResponseText(response));
        }).catch(function (error) { manifestPromise = null; throw error; });
    }
    var manifest = await manifestPromise;
    if (!manifest || !Array.isArray(manifest.queryShards)) return loadIndex();
    if (manifest.queryShards.length > 4096) throw new Error('Too many search shards.');
    var tokens = normalizeText(request.query).split(' ').filter(Boolean);
    var candidates = manifest.queryShards.filter(function (shard) {
      return (!request.project || shard.project === request.project) &&
        tokens.every(function (token) {
          var length = Math.min(3, token.length);
          for (var offset = 0; offset <= token.length - length; offset++)
            if (!shardHasPrefix(shard, token.slice(offset, offset + length))) return false;
          return true;
        });
    });
    var decodedBytes = candidates.reduce(function (total, shard) { return total + Number(shard.bytes || 0); }, 0);
    if (!Number.isFinite(decodedBytes) || decodedBytes > 64 * 1024 * 1024) throw new Error('Search shard selection exceeds the safety limit. Refine the query.');
    var entries = [];
    var actualBytes = 0;
    // Bound concurrent requests and reuse only the shards selected by a query.
    for (var offset = 0; offset < candidates.length; offset += 4) {
      var loaded = await Promise.all(candidates.slice(offset, offset + 4).map(function (shard) {
        var url = new URL(shard.path, manifestUrl);
        if (url.origin !== global.location.origin || url.pathname.indexOf(new URL('query/', manifestUrl).pathname) !== 0) throw new Error('Invalid search shard path.');
        if (!shardPromises[url.href]) {
          shardPromises[url.href] = global.fetch(url.href, { credentials: 'same-origin', cache: 'no-cache' }).then(async function (response) {
            if (!response.ok) throw new Error('Search shard request failed with HTTP ' + response.status + '.');
            var json = await readBoundedResponseText(response);
            var bytes = new TextEncoder().encode(json).byteLength;
            var values = JSON.parse(json);
            if (!Array.isArray(values) || values.length > MAX_INDEX_ENTRIES) throw new Error('Invalid search shard.');
            if (cachedShardBytes + bytes > 64 * 1024 * 1024) { shardPromises = Object.create(null); cachedShardBytes = 0; }
            cachedShardBytes += bytes;
            return { values: values, bytes: bytes };
          }).catch(function (error) { delete shardPromises[url.href]; throw error; });
        }
        return shardPromises[url.href];
      }));
      loaded.forEach(function (shard) {
        actualBytes += shard.bytes;
        if (actualBytes > 64 * 1024 * 1024 || entries.length + shard.values.length > 200000) throw new Error('Search results exceed the safety limit. Refine the query.');
        entries = entries.concat(shard.values);
      });
    }
    return entries;
  }

  function shardHasPrefix(shard, prefix) {
    if (/[\uD800-\uDFFF]/.test(prefix)) return true;
    if (Array.isArray(shard.prefixes)) return shard.prefixes.indexOf(prefix) >= 0;
    if (!shard.prefixBloom) return true; // Older manifests without hints remain searchable.
    if (!shard._prefixBits) {
      var encoded = String(shard.prefixBloom);
      if (encoded.length > 131072) throw new Error('Search shard hint exceeds the safety limit.');
      shard._prefixBits = global.atob(encoded);
    }
    var bits = shard._prefixBits;
    if (!bits.length) return true;
    return [2166136261, 3339675911, 1099511627].every(function (seed) {
      var hash = seed;
      for (var index = 0; index < prefix.length; index++) hash = Math.imul(hash ^ prefix.charCodeAt(index), 16777619) >>> 0;
      var bit = hash % (bits.length * 8);
      return (bits.charCodeAt(Math.floor(bit / 8)) & (1 << (bit % 8))) !== 0;
    });
  }

  function normalizeResponse(result, request) {
    result = result || {};
    var source = Array.isArray(result.results) ? result.results : [];
    var shaped = source.map(shapeResult).filter(Boolean);
    var totalMatches = Number.isInteger(result.totalMatches) && result.totalMatches >= source.length
      ? result.totalMatches
      : source.length;
    var response = {
      query: request.query,
      totalMatches: totalMatches,
      returned: 0,
      moreResultsAvailable: totalMatches > 0,
      outputTruncated: source.length > request.limit || shaped.length !== source.length,
      results: []
    };

    shaped.forEach(function (item) {
      if (response.returned >= request.limit) return;
      response.results.push(item);
      response.returned = response.results.length;
      response.moreResultsAvailable = totalMatches > response.returned;
      if (JSON.stringify(response).length > MAX_OUTPUT_CHARACTERS) {
        // Keep the exact link; optional API detail yields to the output budget.
        var compact = { title: item.title, url: item.url, collection: item.collection, kind: item.kind };
        response.results[response.results.length - 1] = compact;
        if (JSON.stringify(response).length <= MAX_OUTPUT_CHARACTERS) { response.outputTruncated = true; return; }
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
    var usesAdapter = Boolean(adapter && typeof adapter.search === 'function');
    var result = usesAdapter
      ? await adapter.search(request)
      : await genericSearch(Object.assign({}, request, { limit: 100 }));
    var response = normalizeResponse(result, request);
    await syncVisibleSearch(response, request, usesAdapter);
    return response;
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
    manifestPromise = null;
    shardPromises = Object.create(null);
    cachedShardBytes = 0;
    if (adapter && typeof adapter.invalidateIndex === 'function') adapter.invalidateIndex();
  };
  api.normalizeText = normalizeText;
  api.searchEntries = searchEntries;
  api.facets = async function () {
    await loadQueryEntries({ query: '', project: '__facets_only__' });
    var manifest = await manifestPromise;
    return { projects: manifest && Array.isArray(manifest.queryShards) ?
      Array.from(new Set(manifest.queryShards.map(function (shard) { return shard.project; }).filter(Boolean))).sort() : [] };
  };
  // The visible search UI shares the same index and ranking, with its own page size.
  api.search = function (request) {
    request = request || {};
    return genericSearch({
      query: boundedText(request.query, 200).trim(),
      limit: boundedInteger(request.limit, 20, 1, 100),
      project: boundedText(request.project, 160),
      kind: boundedText(request.kind, 48),
      signal: request.signal
    });
  };
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
