const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../PowerForge.Web/Assets/WebMcp/site-search.v1.js'), 'utf8');

function runtime(fetch, Worker, json = JSON) {
  const surface = { getAttribute: key => key === 'data-webmcp-tool-name' ? 'search_site' : null, setAttribute() {} };
  const document = { baseURI: 'https://example.test/search/', currentScript: { src: 'https://example.test/assets/site-search.hash.js' }, readyState: 'complete', querySelector: () => surface };
  const window = { location: { origin: 'https://example.test' }, fetch, atob, AbortController, Worker };
  vm.runInNewContext(source, { window, document, URL, TextEncoder, TextDecoder, DOMException, AbortController, JSON: json });
  return window.PowerForgeWebMcpSearch;
}
const response = value => new Response(JSON.stringify(value), { headers: { 'content-type': 'application/json' } });
const descriptors = count => Array.from({ length: count }, (_, i) => ({ path: 'query/' + i + '.json', bytes: 1024, project: 'Example' }));

test('a broad query fails explicitly before downloading any query shards', async () => {
  const urls = [];
  const api = runtime(async url => { urls.push(url); return response({ queryShards: descriptors(65) }); });
  await assert.rejects(api.search({ query: 'table' }), error => error.code === 'SEARCH_QUERY_TOO_BROAD');
  assert.equal(urls.length, 1);
});

test('the byte budget also applies when the request count is small', async () => {
  const shard = descriptors(1)[0]; shard.bytes = 9 * 1024 * 1024;
  const api = runtime(async () => response({ queryShards: [shard] }));
  await assert.rejects(api.search({ query: 'table' }), error => error.code === 'SEARCH_QUERY_TOO_BROAD');
});

test('cancellation stops the next request batch and keeps reusable completed shards', async () => {
  let resolveBatch;
  const batch = new Promise(resolve => { resolveBatch = resolve; });
  let firstBatchReady;
  const ready = new Promise(resolve => { firstBatchReady = resolve; });
  let queryRequests = 0;
  const api = runtime(async url => {
    if (url.endsWith('manifest.json')) return response({ queryShards: descriptors(8) });
    queryRequests++;
    if (queryRequests === 4) firstBatchReady();
    await batch;
    return response([{ title: 'Create document', url: '/api/create/', collection: 'api', project: 'Example' }]);
  });
  const controller = new AbortController();
  const pending = api.search({ query: 'create', signal: controller.signal });
  await ready;
  assert.equal(queryRequests, 4);
  controller.abort();
  resolveBatch();
  await assert.rejects(pending, error => error.name === 'AbortError');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(queryRequests, 4);
  const result = await api.search({ query: 'create', project: 'Example' });
  assert.equal(result.totalMatches, 1);
  assert.equal(result.results[0].url, '/api/create/');
  assert.equal(queryRequests, 8);
});

test('package facets use their small artifact without fetching the manifest', async () => {
  const urls = [];
  const api = runtime(async url => { urls.push(url); return response({ projects: ['Example'] }); });
  assert.equal((await api.facets()).projects[0], 'Example');
  await api.facets();
  assert.deepEqual(urls, ['https://example.test/search/facets.json']);
});

test('exact overload aliases and filters still produce complete matching results', async () => {
  const entries = [
    { title: 'Create', aliases: ['Document.Create'], url: '/api/create/#stream', collection: 'api', project: 'Example', kind: 'method' },
    { title: 'Create', aliases: ['Document.Create'], url: '/api/create/#path', collection: 'api', project: 'Other', kind: 'method' }
  ];
  const api = runtime(async url => response(url.endsWith('manifest.json') ? { queryShards: descriptors(1) } : entries));
  const all = await api.search({ query: 'Document.Create' });
  assert.equal(all.totalMatches, 2);
  const filtered = await api.search({ query: 'Document.Create', project: 'Example', kind: 'method' });
  assert.equal(filtered.totalMatches, 1);
  assert.equal(filtered.results[0].url, '/api/create/#stream');
});

test('the hashed runtime asset parses and ranks shards in worker mode', async () => {
  let workerUrl;
  class Worker {
    constructor(url) {
      workerUrl = url;
      this.self = { postMessage: data => queueMicrotask(() => this.onmessage({ data })) };
      vm.runInNewContext(source, { self: this.self, TextEncoder, TextDecoder, DOMException });
    }
    postMessage(data) { this.self.onmessage({ data }); }
    terminate() {}
  }
  const json = { ...JSON, parse: text => {
    assert.ok(!text.startsWith('['), 'Shard JSON must be parsed in the worker.');
    return JSON.parse(text);
  } };
  const api = runtime(async url => response(url.endsWith('manifest.json') ? { queryShards: descriptors(1) } :
    [{ title: 'Create', url: '/api/create/', collection: 'api', project: 'Example' }]), Worker, json);
  const result = await api.search({ query: 'Create' });
  assert.equal(result.results[0].url, '/api/create/');
  assert.equal(workerUrl, 'https://example.test/assets/site-search.hash.js');
  api.dispose();
});

test('a blocked worker falls back to the same ranking contract', async () => {
  class Worker { constructor() { throw new DOMException('Blocked by policy', 'SecurityError'); } }
  const api = runtime(async url => response(url.endsWith('manifest.json') ? { queryShards: descriptors(1) } :
    [{ title: 'Create', url: '/api/create/', collection: 'api', project: 'Example' }]), Worker);
  assert.equal((await api.search({ query: 'Create' })).results[0].url, '/api/create/');
});
