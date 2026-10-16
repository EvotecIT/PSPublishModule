const { test } = require('node:test');
const assert = require('node:assert/strict');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs/promises');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');

const runFile = promisify(execFile);
const scriptPath = path.resolve(__dirname, '../Compare-WebSitemaps.ps1');

test('AMP discovery preserves sitemap counts and both canonical and migrated mappings', { timeout: 30_000 }, async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'powerforge-sitemap-amp-'));
  let origin;
  const server = http.createServer((request, response) => {
    response.setHeader('Connection', 'close');
    if (request.url === '/legacy.xml') {
      response.setHeader('Content-Type', 'application/xml');
      response.end(`<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
        <url><loc>${origin}/existing/</loc></url>
        <url><loc>${origin}/old/</loc></url>
        <url><loc>${origin}/missing/</loc></url>
      </urlset>`);
    } else if (request.url === '/existing/' || request.url === '/old/') {
      response.setHeader('Content-Type', 'text/html');
      response.end(`<html><head><link rel="amphtml" href="/mobile${request.url}"></head></html>`);
    } else if (request.url === '/missing/') {
      response.setHeader('Content-Type', 'text/html');
      response.end('<html><body>No AMP alternate</body></html>');
    } else {
      response.statusCode = 404;
      response.end('Unknown fixture route');
    }
  });

  try {
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(0, '127.0.0.1', resolve);
    });
    origin = `http://127.0.0.1:${server.address().port}`;
    const newSitemap = path.join(root, 'sitemap.xml');
    await fs.writeFile(newSitemap, `<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
      <url><loc>${origin}/existing/</loc></url>
      <url><loc>${origin}/blog/old/</loc></url>
      <url><loc>${origin}/new-only/</loc></url>
    </urlset>`);

    async function compare(discoverAmpHtml) {
      const resultPath = path.join(root, discoverAmpHtml ? 'amp.json' : 'plain.json');
      const args = ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', scriptPath,
        '-LegacySitemapUrls', `${origin}/legacy.xml`, '-NewSitemapPath', newSitemap,
        '-OutputJsonPath', resultPath, '-TimeoutSec', '3'];
      if (discoverAmpHtml) args.push('-DiscoverAmpHtml');
      const result = await runFile('pwsh', args, {
        encoding: 'utf8', timeout: 15_000,
        env: { ...process.env, NO_PROXY: '127.0.0.1,localhost,::1', NO_COLOR: '1' }
      });
      assert.equal(result.stderr, '');
      return JSON.parse(await fs.readFile(resultPath, 'utf8'));
    }

    const plain = await compare(false);
    const amp = await compare(true);
    assert.equal(plain.LegacyUrlCount, 3);
    assert.equal(plain.ExactOverlapCount, 1);
    assert.equal(amp.LegacyUrlCount, plain.LegacyUrlCount);
    assert.equal(amp.ExactOverlapCount, plain.ExactOverlapCount);
    assert.equal(amp.MissingLegacyCount, plain.MissingLegacyCount);
    assert.deepEqual(amp.NewOnlyUrls, plain.NewOnlyUrls);
    assert.equal(amp.AmpDiscoveryCount, 2);
    assert.deepEqual(amp.AmpDiscovery.map(row => row.MatchKind).sort(),
      ['amphtml-to-canonical', 'amphtml-to-root-to-blog']);
    assert.equal(amp.AmpDiscovery.find(row => row.MatchKind === 'amphtml-to-canonical').TargetUrl,
      `${origin}/existing/`);
    assert.equal(amp.AmpDiscovery.find(row => row.MatchKind === 'amphtml-to-root-to-blog').TargetUrl,
      `${origin}/blog/old/`);
  } finally {
    if (server.listening) {
      const closed = new Promise(resolve => server.close(resolve));
      server.closeAllConnections();
      await closed;
    }
    await fs.rm(root, { recursive: true });
  }
});
