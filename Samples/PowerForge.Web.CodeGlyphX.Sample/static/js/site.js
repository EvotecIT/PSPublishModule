// Theme management functions for Blazor interop
function getTheme() {
    return localStorage.getItem('theme') || 'dark';
}

function setTheme(theme) {
    localStorage.setItem('theme', theme);
    document.documentElement.dataset.theme = theme;
}

// Expose to global scope for Blazor JS interop
globalThis.getTheme = getTheme;
globalThis.setTheme = setTheme;

(() => {
    function copyText(text) {
        if (!text) return;
        if (navigator.clipboard?.writeText) {
            navigator.clipboard.writeText(text).catch(() => { });
            return;
        }
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.setAttribute('readonly', 'readonly');
        textarea.style.position = 'absolute';
        textarea.style.left = '-9999px';
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand('copy'); // Fallback for older browsers
        } catch {
            // ignore
        }
        textarea.remove();
    }

    function loadPrism(callback) {
        if (globalThis.__codeGlyphPrismLoaded) {
            callback?.();
            return;
        }
        const css = document.createElement('link');
        css.rel = 'stylesheet';
        css.href = '/vendor/prism/prism-tomorrow.min.css';
        document.head.appendChild(css);

        const script = document.createElement('script');
        script.src = '/vendor/prism/prism.min.js';
        script.onload = () => {
            const csharp = document.createElement('script');
            csharp.src = '/vendor/prism/prism-csharp.min.js';
            csharp.onload = () => {
                globalThis.__codeGlyphPrismLoaded = true;
                callback?.();
            };
            document.head.appendChild(csharp);
        };
        document.head.appendChild(script);
    }

    function createCopyButton(textProvider, options) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = options?.className || 'copy-btn';
        btn.title = options?.title || 'Copy to clipboard';
        btn.setAttribute('aria-label', options?.title || 'Copy to clipboard');
        btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/></svg>';
        btn.addEventListener('click', () => {
            const code = textProvider() || '';
            copyText(code);
            btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"/></svg>';
            btn.classList.add('copied');
            setTimeout(() => {
                btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/></svg>';
                btn.classList.remove('copied');
            }, 2000);
        });
        return btn;
    }

    function addCopyButton(block) {
        if (block.querySelector('.copy-btn')) return;
        block.classList.add('copyable');
        block.style.position = 'relative';
        const codeEl = block.querySelector('code');
        const rawText = (codeEl ? codeEl.textContent : block.textContent) || '';
        const btn = createCopyButton(() => rawText);
        block.appendChild(btn);
    }

    function initCodeBlocks() {
        const blocks = Array.from(document.querySelectorAll('pre.code-block, .docs-content pre, .docs-static pre, .type-detail pre'))
            .filter((block) => !block.classList.contains('initialized'));
        const signatures = Array.from(document.querySelectorAll('code.signature'))
            .filter((sig) => !sig.classList.contains('initialized'));

        if (!blocks.length && !signatures.length) return;

        // Separate blocks that need highlighting from those that don't
        const blocksNeedingHighlight = [];
        const blocksReady = [];

        blocks.forEach((block) => {
            block.classList.add('initialized');
            const needsHighlight = block.classList.contains('code-block') && !block.querySelector('.keyword, .string, .comment');
            if (needsHighlight) {
                blocksNeedingHighlight.push(block);
            } else {
                blocksReady.push(block);
            }
        });

        // Add copy buttons to blocks that don't need highlighting
        blocksReady.forEach(addCopyButton);

        signatures.forEach((sig) => {
            sig.classList.add('initialized', 'copyable');
            sig.style.position = 'relative';
            sig.style.paddingRight = '2.75rem';
            const rawText = sig.textContent || '';
            const btn = createCopyButton(() => rawText, { className: 'copy-btn copy-btn-inline' });
            sig.appendChild(btn);
        });

        // Highlight blocks first, then add copy buttons
        if (blocksNeedingHighlight.length) {
            loadPrism(() => {
                blocksNeedingHighlight.forEach((block) => {
                    if (!block.classList.contains('prism-highlighted')) {
                        block.classList.add('language-csharp', 'prism-highlighted');
                        globalThis.Prism?.highlightElement?.(block);
                    }
                    // Add copy button AFTER highlighting
                    addCopyButton(block);
                });
            });
        }
    }

    let benchSummaryPromise = null;
    function loadBenchmarkSummary() {
        if (!benchSummaryPromise) {
            benchSummaryPromise = fetch('/data/benchmark-summary.json', { cache: 'no-store' })
                .then((res) => (res.ok ? res.json() : null))
                .catch(() => null);
        }
        return benchSummaryPromise;
    }

    function pickBenchmarkSummary(data) {
        if (!data) return null;
        const order = [
            ['windows', 'quick'],
            ['windows', 'full'],
            ['linux', 'quick'],
            ['linux', 'full'],
            ['macos', 'quick'],
            ['macos', 'full'],
        ];
        for (const [os, mode] of order) {
            const entry = data?.[os]?.[mode];
            if (entry?.summary?.length) return entry;
        }
        for (const osKey of Object.keys(data)) {
            const osEntry = data[osKey];
            for (const modeKey of Object.keys(osEntry || {})) {
                const entry = osEntry?.[modeKey];
                if (entry?.summary?.length) return entry;
            }
        }
        return null;
    }

    function appendVendorCell(td, vendor, delta) {
        if (!vendor) return;
        if (vendor.mean) {
            const mean = document.createElement('div');
            mean.textContent = vendor.mean;
            td.appendChild(mean);
        }
        if (vendor.allocated) {
            const alloc = document.createElement('div');
            alloc.className = 'bench-dim';
            alloc.textContent = vendor.allocated;
            td.appendChild(alloc);
        }
        if (delta) {
            const d = document.createElement('div');
            d.className = 'bench-delta';
            d.textContent = delta;
            td.appendChild(d);
        }
    }

    function renderBenchmarkSummary() {
        const container = document.querySelector('[data-benchmark-summary]');
        if (!container || container.dataset.loaded === 'true') return;

        loadBenchmarkSummary().then((data) => {
            const entry = pickBenchmarkSummary(data);
            if (!entry || !entry.summary?.length) {
                container.textContent = 'Benchmark summary unavailable.';
                container.dataset.loaded = 'true';
                return;
            }

            const table = document.createElement('table');
            table.className = 'bench-table bench-summary-table';

            const thead = document.createElement('thead');
            thead.innerHTML = '<tr>' +
                '<th>Benchmark</th>' +
                '<th>Scenario</th>' +
                '<th>Fastest</th>' +
                '<th>CodeGlyphX</th>' +
                '<th>ZXing.Net</th>' +
                '<th>QRCoder</th>' +
                '<th>Barcoder</th>' +
                '<th>CodeGlyphX vs Fastest</th>' +
                '<th>Alloc vs Fastest</th>' +
                '<th>Rating</th>' +
                '</tr>';
            table.appendChild(thead);

            const tbody = document.createElement('tbody');
            entry.summary.forEach((item) => {
                const row = document.createElement('tr');
                const vendors = item.vendors || {};
                const deltas = item.deltas || {};

                const cells = [
                    item.benchmark || '',
                    item.scenario || '',
                    item.fastestVendor ? `${item.fastestVendor} ${item.fastestMean || ''}`.trim() : (item.fastestMean || ''),
                ];

                cells.forEach((text) => {
                    const td = document.createElement('td');
                    td.textContent = text;
                    row.appendChild(td);
                });

                const cgxTd = document.createElement('td');
                appendVendorCell(cgxTd, vendors['CodeGlyphX'], '');
                row.appendChild(cgxTd);

                const zxTd = document.createElement('td');
                appendVendorCell(zxTd, vendors['ZXing.Net'], deltas['ZXing.Net']);
                row.appendChild(zxTd);

                const qrcTd = document.createElement('td');
                appendVendorCell(qrcTd, vendors['QRCoder'], deltas['QRCoder']);
                row.appendChild(qrcTd);

                const barTd = document.createElement('td');
                appendVendorCell(barTd, vendors['Barcoder'], deltas['Barcoder']);
                row.appendChild(barTd);

                const ratioTd = document.createElement('td');
                ratioTd.textContent = item.codeGlyphXVsFastestText || '';
                row.appendChild(ratioTd);

                const allocTd = document.createElement('td');
                allocTd.textContent = item.codeGlyphXAllocVsFastestText || '';
                row.appendChild(allocTd);

                const ratingTd = document.createElement('td');
                ratingTd.textContent = item.rating || '';
                row.appendChild(ratingTd);

                tbody.appendChild(row);
            });
            table.appendChild(tbody);

            container.innerHTML = '';
            container.appendChild(table);
            container.dataset.loaded = 'true';
        });
    }

    document.addEventListener('click', (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;
        const button = target.closest('[data-copy]');
        if (!button) return;
        const text = button.dataset.copy;
        if (!text) return;
        copyText(text);
    });

    let initTimer = 0;
    function scheduleInit() {
        if (initTimer) return;
        initTimer = globalThis.setTimeout(() => {
            initTimer = 0;
            initCodeBlocks();
            renderBenchmarkSummary();
        }, 100);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scheduleInit);
    } else {
        scheduleInit();
    }

    const observer = new MutationObserver(scheduleInit);
    observer.observe(document.body, { childList: true, subtree: true });

    globalThis.CodeGlyphX = globalThis.CodeGlyphX || {};
    globalThis.CodeGlyphX.initCodeBlocks = initCodeBlocks;
    globalThis.CodeGlyphX.renderBenchmarkSummary = renderBenchmarkSummary;
})();


// Theme toggle (Auto/Light/Dark)
(function() {
  function updateActiveTheme(theme) {
    document.querySelectorAll('.theme-toggle button').forEach(function(btn) {
      btn.classList.toggle('active', btn.dataset.theme === theme);
    });
  }

  // Set initial active state
  var currentTheme = document.documentElement.dataset.theme || 'auto';
  updateActiveTheme(currentTheme);

  // Handle theme button clicks
  document.querySelectorAll('.theme-toggle button[data-theme]').forEach(function(btn) {
    btn.addEventListener('click', function() {
      var theme = this.dataset.theme;
      document.documentElement.dataset.theme = theme;
      localStorage.setItem('theme', theme);
      updateActiveTheme(theme);
    });
  });
})();

// Keyboard focus visibility (show focus ring only for keyboard navigation)
function enableKeyboardFocus() { document.body.classList.add('using-keyboard'); }
function disableKeyboardFocus() { document.body.classList.remove('using-keyboard'); }
globalThis.addEventListener('keydown', function(e) {
  if (e.key === 'Tab' || e.key === 'ArrowUp' || e.key === 'ArrowDown' || e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
    enableKeyboardFocus();
  }
});
globalThis.addEventListener('mousedown', disableKeyboardFocus, true);
globalThis.addEventListener('touchstart', disableKeyboardFocus, true);

// Mobile nav toggle
const navToggle = document.getElementById('nav-toggle');
if (navToggle) {
  navToggle.addEventListener('change', function() {
    document.body.classList.toggle('nav-open', this.checked);
  });
}

// Benchmark summary renderer
(function() {
  let benchSummaryPromise = null;
  function loadBenchmarkSummary() {
    if (!benchSummaryPromise) {
      benchSummaryPromise = fetch('/data/benchmark-summary.json', { cache: 'no-store' })
        .then(function(res) { return res.ok ? res.json() : null; })
        .catch(function() { return null; });
    }
    return benchSummaryPromise;
  }

  function pickBenchmarkSummary(data) {
    if (!data) return null;
    var order = [
      ['windows', 'quick'],
      ['windows', 'full'],
      ['linux', 'quick'],
      ['linux', 'full'],
      ['macos', 'quick'],
      ['macos', 'full']
    ];
    for (var i = 0; i < order.length; i++) {
      var os = order[i][0];
      var mode = order[i][1];
      var entry = data && data[os] && data[os][mode];
      if (entry && entry.summary && entry.summary.length) return entry;
    }
    var keys = Object.keys(data || {});
    for (var k = 0; k < keys.length; k++) {
      var osEntry = data[keys[k]] || {};
      var modes = Object.keys(osEntry || {});
      for (var m = 0; m < modes.length; m++) {
        var modeEntry = osEntry[modes[m]];
        if (modeEntry && modeEntry.summary && modeEntry.summary.length) return modeEntry;
      }
    }
    return null;
  }

  function appendVendorCell(td, vendor, delta) {
    if (!vendor) return;
    if (vendor.mean) {
      var mean = document.createElement('div');
      mean.textContent = vendor.mean;
      td.appendChild(mean);
    }
    if (vendor.allocated) {
      var alloc = document.createElement('div');
      alloc.className = 'bench-dim';
      alloc.textContent = vendor.allocated;
      td.appendChild(alloc);
    }
    if (delta) {
      var d = document.createElement('div');
      d.className = 'bench-delta';
      d.textContent = delta;
      td.appendChild(d);
    }
  }

  function renderBenchmarkSummary() {
    var container = document.querySelector('[data-benchmark-summary]');
    if (!container || container.dataset.loaded === 'true') return;

    loadBenchmarkSummary().then(function(data) {
      var entry = pickBenchmarkSummary(data);
      if (!entry || !entry.summary || !entry.summary.length) {
        container.textContent = 'Benchmark summary unavailable.';
        container.dataset.loaded = 'true';
        return;
      }

      var table = document.createElement('table');
      table.className = 'bench-table bench-summary-table';

      var thead = document.createElement('thead');
      thead.innerHTML = '<tr>' +
        '<th>Benchmark</th>' +
        '<th>Scenario</th>' +
        '<th>Fastest</th>' +
        '<th>CodeGlyphX</th>' +
        '<th>ZXing.Net</th>' +
        '<th>QRCoder</th>' +
        '<th>Barcoder</th>' +
        '<th>CodeGlyphX vs Fastest</th>' +
        '<th>Alloc vs Fastest</th>' +
        '<th>Rating</th>' +
        '</tr>';
      table.appendChild(thead);

      var tbody = document.createElement('tbody');
      entry.summary.forEach(function(item) {
        var row = document.createElement('tr');
        var vendors = item.vendors || {};
        var deltas = item.deltas || {};

        var cells = [
          item.benchmark || '',
          item.scenario || '',
          item.fastestVendor ? (item.fastestVendor + ' ' + (item.fastestMean || '')).trim() : (item.fastestMean || '')
        ];

        cells.forEach(function(text) {
          var td = document.createElement('td');
          td.textContent = text;
          row.appendChild(td);
        });

        var cgxTd = document.createElement('td');
        appendVendorCell(cgxTd, vendors['CodeGlyphX'], '');
        row.appendChild(cgxTd);

        var zxTd = document.createElement('td');
        appendVendorCell(zxTd, vendors['ZXing.Net'], deltas['ZXing.Net']);
        row.appendChild(zxTd);

        var qrcTd = document.createElement('td');
        appendVendorCell(qrcTd, vendors['QRCoder'], deltas['QRCoder']);
        row.appendChild(qrcTd);

        var barTd = document.createElement('td');
        appendVendorCell(barTd, vendors['Barcoder'], deltas['Barcoder']);
        row.appendChild(barTd);

        var ratioTd = document.createElement('td');
        ratioTd.textContent = item.codeGlyphXVsFastestText || '';
        row.appendChild(ratioTd);

        var allocTd = document.createElement('td');
        allocTd.textContent = item.codeGlyphXAllocVsFastestText || '';
        row.appendChild(allocTd);

        var ratingTd = document.createElement('td');
        ratingTd.textContent = item.rating || '';
        row.appendChild(ratingTd);

        tbody.appendChild(row);
      });
      table.appendChild(tbody);

      container.innerHTML = '';
      container.appendChild(table);
      container.dataset.loaded = 'true';
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', renderBenchmarkSummary);
  } else {
    renderBenchmarkSummary();
  }
})();


// Docs sidebar toggle (static pages)
const docsToggle = document.querySelector('.docs-sidebar-toggle');
const docsSidebar = document.querySelector('.docs-sidebar');
const docsOverlay = document.querySelector('.docs-sidebar-overlay');

if (docsToggle && docsSidebar) {
  docsToggle.addEventListener('click', function() {
    docsSidebar.classList.toggle('sidebar-open');
    if (docsOverlay) { docsOverlay.classList.toggle('active'); }
  });
}

if (docsOverlay && docsSidebar) {
  docsOverlay.addEventListener('click', function() {
    docsSidebar.classList.remove('sidebar-open');
    docsOverlay.classList.remove('active');
  });
}


document.querySelectorAll('[data-style-board]').forEach(function (board) {
  const src = board.dataset.styleBoardSrc || '/data/style-board.json';
  const base = board.dataset.styleBoardBase || '/assets/style-board/';

  function addCard(item) {
    const card = document.createElement('div');
    card.className = 'style-board-card';

    const img = document.createElement('img');
    img.loading = 'lazy';
    img.alt = item.name || 'QR style';
    img.src = base + item.file;
    card.appendChild(img);

    const label = document.createElement('div');
    label.className = 'style-board-label';
    label.textContent = item.name || 'QR Style';
    card.appendChild(label);

    board.appendChild(card);
  }

  fetch(src)
    .then(function (res) {
      if (!res.ok) throw new Error('Failed to load style board');
      return res.json();
    })
    .then(function (items) {
      if (!Array.isArray(items)) return;
      board.innerHTML = '';
      items.forEach(addCard);
    })
    .catch(function () {
      board.innerHTML = '<div class="style-board-fallback">Style board assets not available yet.</div>';
    });
});


// Showcase carousel functionality
document.querySelectorAll('.showcase-gallery').forEach(function(gallery) {
  const carouselId = gallery.dataset.carousel;
  const dataScript = document.querySelector('script.carousel-data[data-carousel="' + carouselId + '"]');
  if (!dataScript) return;

  const captions = JSON.parse(dataScript.textContent);
  let currentTheme = 'dark';
  let currentSlide = 0;

  const themeTabs = gallery.querySelectorAll('.showcase-gallery-tab');
  const slides = gallery.querySelectorAll('.showcase-carousel-slide');
  const prevBtn = gallery.querySelector('.showcase-carousel-nav.prev');
  const nextBtn = gallery.querySelector('.showcase-carousel-nav.next');
  const dots = gallery.querySelectorAll('.showcase-carousel-dot');
  const thumbContainers = gallery.querySelectorAll('.showcase-carousel-thumbs');
  const captionEl = gallery.querySelector('.showcase-carousel-caption');
  const counterEl = gallery.querySelector('.showcase-carousel-counter');

  function updateCarousel() {
    const themeCaptions = captions[currentTheme];
    const totalSlides = themeCaptions.length;

    // Update slides visibility
    slides.forEach(function(slide) {
      const isCurrentTheme = slide.dataset.theme === currentTheme;
      const isCurrentSlide = Number.parseInt(slide.dataset.index, 10) === currentSlide;
      slide.style.display = isCurrentTheme ? '' : 'none';
      slide.classList.toggle('active', isCurrentTheme && isCurrentSlide);
    });

    // Update dots
    dots.forEach(function(dot, idx) {
      dot.classList.toggle('active', idx === currentSlide);
    });

    // Update thumbnails
    thumbContainers.forEach(function(container) {
      const isCurrentTheme = container.dataset.themeContainer === currentTheme;
      container.style.display = isCurrentTheme ? '' : 'none';
      if (isCurrentTheme) {
        container.querySelectorAll('.showcase-carousel-thumb').forEach(function(thumb, idx) {
          thumb.classList.toggle('active', idx === currentSlide);
        });
      }
    });

    // Update caption and counter
    if (captionEl) captionEl.textContent = themeCaptions[currentSlide];
    if (counterEl) counterEl.textContent = (currentSlide + 1) + ' / ' + totalSlides;
  }

  function goToSlide(index) {
    const totalSlides = captions[currentTheme].length;
    currentSlide = ((index % totalSlides) + totalSlides) % totalSlides;
    updateCarousel();
  }

  // Theme tab clicks
  themeTabs.forEach(function(tab) {
    tab.addEventListener('click', function() {
      currentTheme = tab.dataset.theme;
      currentSlide = 0;
      themeTabs.forEach(function(t) { t.classList.remove('active'); });
      tab.classList.add('active');
      updateCarousel();
    });
  });

  // Navigation buttons
  if (prevBtn) prevBtn.addEventListener('click', function() { goToSlide(currentSlide - 1); });
  if (nextBtn) nextBtn.addEventListener('click', function() { goToSlide(currentSlide + 1); });

  // Dot clicks
  dots.forEach(function(dot) {
    dot.addEventListener('click', function() {
      goToSlide(Number.parseInt(dot.dataset.index, 10));
    });
  });

  // Thumbnail clicks
  thumbContainers.forEach(function(container) {
    container.querySelectorAll('.showcase-carousel-thumb').forEach(function(thumb) {
      thumb.addEventListener('click', function() {
        goToSlide(Number.parseInt(thumb.dataset.index, 10));
      });
    });
  });
});


// Benchmark page renderer
(function() {
  let currentMode = 'quick';
  let currentOs = 'windows';
  let summaryData = null;
  let detailData = null;

  function loadJson(url) {
    return fetch(url, { cache: 'no-store' })
      .then(function(res) { return res.ok ? res.json() : null; })
      .catch(function() { return null; });
  }

  function escapeHtml(text) {
    if (!text) return '';
    return String(text).replace(/[&<>"']/g, function(m) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m];
    });
  }

  function getRatingIcon(rating) {
    switch (rating) {
      case 'good':
        return '<span class="rating-icon rating-good" aria-label="Good">●</span>';
      case 'ok':
        return '<span class="rating-icon rating-ok" aria-label="Okay">●</span>';
      case 'bad':
        return '<span class="rating-icon rating-bad" aria-label="Needs improvement">●</span>';
      default:
        return '<span class="rating-icon">-</span>';
    }
  }

  function getEntry(data, os, mode) {
    return data?.[os]?.[mode] ?? null;
  }

  function findBestEntry(data) {
    // Priority: windows full > windows quick > linux full > linux quick > macos
    const order = [
      ['windows', 'full'], ['windows', 'quick'],
      ['linux', 'full'], ['linux', 'quick'],
      ['macos', 'full'], ['macos', 'quick']
    ];
    for (let i = 0; i < order.length; i++) {
      const entry = getEntry(data, order[i][0], order[i][1]);
      if (entry && (entry.summary?.length || entry.comparisons?.length)) {
        currentOs = order[i][0];
        currentMode = order[i][1];
        return entry;
      }
    }
    return null;
  }

  function formatDate(isoString) {
    if (!isoString) return 'Unknown';
    try {
      const d = new Date(isoString);
      return d.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
    } catch {
      return isoString;
    }
  }

  function formatMode(mode) {
    if (!mode) return 'Unknown';
    return mode.charAt(0).toUpperCase() + mode.slice(1);
  }

  function renderMeta(entry) {
    const container = document.querySelector('[data-benchmark-meta]');
    if (!container) return;

    if (!entry) {
      container.innerHTML = '<p class="benchmark-warning">No benchmark data available.</p>';
      return;
    }

    const meta = entry.meta || {};
    let html = '<div class="benchmark-meta-grid">';
    html += '<div class="meta-item"><span class="meta-label">Last Updated</span><span class="meta-value">' + escapeHtml(formatDate(entry.generatedUtc)) + '</span></div>';
    html += '<div class="meta-item"><span class="meta-label">Mode</span><span class="meta-value">' + escapeHtml(formatMode(entry.runMode)) + '</span></div>';
    html += '<div class="meta-item"><span class="meta-label">OS</span><span class="meta-value">' + escapeHtml(entry.os || 'unknown') + '</span></div>';
    html += '<div class="meta-item"><span class="meta-label">Framework</span><span class="meta-value">' + escapeHtml(entry.framework || 'unknown') + '</span></div>';
    html += '</div>';
    container.innerHTML = html;
  }

  function renderModeSelector(summaryData, detailData) {
    const buttons = document.querySelectorAll('.benchmark-mode-btn');
    const noteEl = document.querySelector('[data-mode-note]');

    buttons.forEach(function(btn) {
      const mode = btn.dataset.mode;
      const summaryEntry = getEntry(summaryData, currentOs, mode);
      const detailEntry = getEntry(detailData, currentOs, mode);
      const hasData = summaryEntry?.summary?.length || detailEntry?.comparisons?.length;

      btn.disabled = !hasData;
      btn.classList.toggle('active', mode === currentMode);

      if (!hasData) {
        btn.title = 'No ' + mode + ' benchmark data available yet';
      } else {
        btn.title = '';
      }

      btn.onclick = function() {
        if (this.disabled) return;
        currentMode = mode;
        buttons.forEach(function(b) { b.classList.toggle('active', b.dataset.mode === mode); });
        renderAll();
      };
    });

    // Update note
    if (noteEl) {
      const entry = getEntry(summaryData, currentOs, currentMode);
      if (entry && entry.runModeDetails) {
        noteEl.textContent = entry.runModeDetails;
      } else if (currentMode === 'quick') {
        noteEl.textContent = 'Quick mode: Fewer iterations, higher variance. Use for rough estimates only.';
      } else {
        noteEl.textContent = 'Full mode: BenchmarkDotNet defaults with statistical analysis.';
      }
    }
  }

  function renderVendorCell(vendor, delta, extraClass) {
    if (!vendor || (!vendor.mean && !vendor.allocated)) {
      return '<td class="bench-na">-</td>';
    }
    const cls = extraClass ? ' class="' + extraClass + '"' : '';
    let html = '<td' + cls + '>';
    if (vendor.mean) {
      html += '<div>' + escapeHtml(vendor.mean) + '</div>';
    }
    if (vendor.allocated) {
      html += '<div class="bench-dim">' + escapeHtml(vendor.allocated) + '</div>';
    }
    if (delta) {
      html += '<div class="bench-dim bench-delta">' + escapeHtml(delta) + '</div>';
    }
    html += '</td>';
    return html;
  }

  function renderSummaryTable(entry) {
    const container = document.querySelector('[data-benchmark-summary]');
    if (!container) return;

    if (!entry || !entry.summary || !entry.summary.length) {
      container.innerHTML = '<p class="benchmark-no-data">No comparison summary available for this mode.</p>';
      return;
    }

    const vendorSet = {};
    entry.summary.forEach(function(item) {
      if (item.vendors) {
        Object.keys(item.vendors).forEach(function(v) {
          if (v !== 'CodeGlyphX') vendorSet[v] = true;
        });
      }
    });
    const preferred = ['ZXing.Net', 'QRCoder', 'Barcoder'];
    const vendorList = preferred.filter(function(v) { return vendorSet[v]; });
    Object.keys(vendorSet).forEach(function(v) {
      if (preferred.indexOf(v) === -1) vendorList.push(v);
    });

    let html = '<div class="table-scroll"><table class="bench-table bench-summary-table">';
    html += '<thead><tr>';
    html += '<th>Benchmark</th>';
    html += '<th>Scenario</th>';
    html += '<th>Fastest</th>';
    html += '<th>CodeGlyphX</th>';
    vendorList.forEach(function(v) {
      html += '<th>' + escapeHtml(v) + '</th>';
    });
    html += '<th>CodeGlyphX vs Fastest</th>';
    html += '<th>Alloc vs Fastest</th>';
    html += '<th>Rating</th>';
    html += '</tr></thead><tbody>';

    entry.summary.forEach(function(item) {
      html += '<tr>';
      html += '<td>' + escapeHtml(item.benchmark || '') + '</td>';
      html += '<td>' + escapeHtml(item.scenario || '') + '</td>';

      html += '<td class="bench-fastest">';
      html += '<div>' + escapeHtml(item.fastestVendor || '') + '</div>';
      if (item.fastestMean) html += '<div class="bench-dim">' + escapeHtml(item.fastestMean) + '</div>';
      html += '</td>';

      const cgx = item.vendors?.['CodeGlyphX'] || {};
      const isCgxFastest = item.fastestVendor === 'CodeGlyphX';
      html += renderVendorCell(cgx, '', isCgxFastest ? 'bench-winner' : '');

      vendorList.forEach(function(v) {
        const vendor = item.vendors?.[v];
        const delta = item.deltas?.[v] || '';
        html += renderVendorCell(vendor, delta, '');
      });

      html += '<td>' + escapeHtml(item.codeGlyphXVsFastestText || (item.codeGlyphXVsFastest ? item.codeGlyphXVsFastest + ' x' : '-')) + '</td>';
      html += '<td>' + escapeHtml(item.codeGlyphXAllocVsFastestText || (item.codeGlyphXAllocVsFastest ? item.codeGlyphXAllocVsFastest + ' x' : '-')) + '</td>';

      const ratingClass = 'bench-rating-' + (item.rating || 'unknown');
      const ratingIcon = getRatingIcon(item.rating);
      html += '<td class="' + ratingClass + '" title="' + escapeHtml(item.rating || '') + '">' + ratingIcon + '</td>';
      html += '</tr>';
    });

    html += '</tbody></table></div>';

    // Add legend
    html += '<div class="bench-legend">';
    html += '<span class="bench-legend-item">' + getRatingIcon('good') + ' within 10% time, 25% allocation</span>';
    html += '<span class="bench-legend-item">' + getRatingIcon('ok') + ' within 50% time, 100% allocation</span>';
    html += '<span class="bench-legend-item">' + getRatingIcon('bad') + ' outside these bounds</span>';
    html += '</div>';

    container.innerHTML = html;
  }

  function renderDetails(entry) {
    const container = document.querySelector('[data-benchmark-details]');
    if (!container) return;

    if (!entry || !entry.comparisons || !entry.comparisons.length) {
      container.innerHTML = '<p class="benchmark-no-data">No detailed comparison data available for this mode.</p>';
      return;
    }

    let html = '';
    entry.comparisons.forEach(function(comp) {
      html += '<div class="benchmark-detail-section">';
      html += '<h3>' + escapeHtml(comp.title) + '</h3>';

      if (!comp.scenarios || !comp.scenarios.length) {
        html += '<p class="bench-na">No scenarios</p>';
        html += '</div>';
        return;
      }

      html += '<div class="table-scroll"><table class="bench-table bench-detail-table">';
      html += '<thead><tr><th>Scenario</th>';

      // Collect all vendors from all scenarios
      const allVendors = {};
      comp.scenarios.forEach(function(s) {
        if (s.vendors) {
          Object.keys(s.vendors).forEach(function(v) { allVendors[v] = true; });
        }
      });
      const vendorList = Object.keys(allVendors).sort(function(a, b) {
        if (a === 'CodeGlyphX') return -1;
        if (b === 'CodeGlyphX') return 1;
        return a.localeCompare(b);
      });

      vendorList.forEach(function(v) {
        html += '<th>' + escapeHtml(v) + '</th>';
      });
      html += '</tr></thead><tbody>';

      comp.scenarios.forEach(function(scenario) {
        html += '<tr>';
        html += '<td>' + escapeHtml(scenario.name) + '</td>';

        vendorList.forEach(function(v) {
          const vendor = scenario.vendors?.[v];
          if (vendor && (vendor.mean || vendor.allocated)) {
            html += '<td>';
            if (vendor.mean) {
              html += '<div>' + escapeHtml(vendor.mean) + '</div>';
            }
            if (vendor.allocated) {
              html += '<div class="bench-dim">' + escapeHtml(vendor.allocated) + '</div>';
            }
            const delta = scenario.deltas?.[v];
            if (delta) {
              html += '<div class="bench-dim bench-delta">' + escapeHtml(delta) + '</div>';
            }
            html += '</td>';
          } else {
            html += '<td class="bench-na">-</td>';
          }
        });
        html += '</tr>';
      });

      html += '</tbody></table></div>';
      html += '</div>';
    });

    container.innerHTML = html;
  }

  function renderBaseline(entry) {
    const container = document.querySelector('[data-benchmark-baseline]');
    if (!container) return;

    if (!entry || !entry.baselines || !entry.baselines.length) {
      container.innerHTML = '<p class="benchmark-no-data">No baseline data available for this mode.</p>';
      return;
    }

    let html = '';
    entry.baselines.forEach(function(baseline) {
      html += '<div class="benchmark-detail-section">';
      html += '<h3>' + escapeHtml(baseline.title) + '</h3>';

      if (!baseline.rows || !baseline.rows.length) {
        html += '<p class="bench-na">No data</p>';
        html += '</div>';
        return;
      }

      html += '<div class="table-scroll"><table class="bench-table bench-baseline-table">';
      html += '<thead><tr><th>Scenario</th><th>Mean</th><th>Allocated</th></tr></thead>';
      html += '<tbody>';

      baseline.rows.forEach(function(row) {
        html += '<tr>';
        html += '<td>' + escapeHtml(row.scenario) + '</td>';
        html += '<td>' + escapeHtml(row.mean || '-') + '</td>';
        html += '<td>' + escapeHtml(row.allocated || '-') + '</td>';
        html += '</tr>';
      });

      html += '</tbody></table></div>';
      html += '</div>';
    });

    container.innerHTML = html;
  }

  function renderEnvironment(entry) {
    const container = document.querySelector('[data-benchmark-environment]');
    if (!container) return;

    if (!entry || !entry.meta) {
      container.innerHTML = '<p class="benchmark-no-data">No environment info available.</p>';
      return;
    }

    const meta = entry.meta;
    let html = '<div class="benchmark-env-grid">';

    const items = [
      ['OS', meta.osDescription],
      ['Architecture', meta.osArchitecture || meta.processArchitecture],
      ['.NET SDK', meta.dotnetSdk],
      ['Runtime', meta.runtime],
      ['Processors', meta.processorCount]
    ];

    items.forEach(function(item) {
      if (item[1]) {
        html += '<div class="env-item"><span class="env-label">' + escapeHtml(item[0]) + '</span>';
        html += '<span class="env-value">' + escapeHtml(item[1]) + '</span></div>';
      }
    });

    html += '</div>';
    container.innerHTML = html;
  }

  function renderNotes(entry) {
    const container = document.querySelector('[data-benchmark-notes]');
    if (!container || !entry || !entry.notes || !entry.notes.length) return;

    let html = '<ul>';
    entry.notes.forEach(function(note) {
      if (!note || !note.trim()) return;
      html += '<li>' + escapeHtml(note) + '</li>';
    });
    html += '</ul>';
    container.innerHTML = html;
  }

  function parseTimeValue(str) {
    if (!str) return null;
    // Parse values like "1.234 ms", "567.8 μs", "12.3 ns"
    const match = str.match(/^([\d.]+)\s*(ms|μs|us|ns)/i);
    if (!match) return null;
    const value = parseFloat(match[1]);
    const unit = match[2].toLowerCase();
    // Convert to microseconds for comparison
    if (unit === 'ms') return value * 1000;
    if (unit === 'μs' || unit === 'us') return value;
    if (unit === 'ns') return value / 1000;
    return value;
  }

  function renderCharts(entry) {
    const container = document.querySelector('[data-benchmark-charts]');
    if (!container) return;

    if (!entry || !entry.summary || !entry.summary.length) {
      container.innerHTML = '<p class="benchmark-no-data">No chart data available for this mode.</p>';
      return;
    }

    // Collect chart data from summary
    const chartData = [];
    entry.summary.forEach(function(item) {
      const scenario = item.scenario || item.benchmark || '';
      const cgxMean = item.vendors?.['CodeGlyphX']?.mean || item.codeGlyphXMean;
      const fastestMean = item.fastestMean;
      const fastestVendor = item.fastestVendor;

      if (cgxMean && fastestMean) {
        const cgxValue = parseTimeValue(cgxMean);
        const fastestValue = parseTimeValue(fastestMean);
        if (cgxValue && fastestValue) {
          chartData.push({
            scenario: scenario,
            cgxValue: cgxValue,
            cgxLabel: cgxMean,
            fastestValue: fastestValue,
            fastestLabel: fastestMean,
            fastestVendor: fastestVendor,
            isCgxFastest: fastestVendor === 'CodeGlyphX',
            rating: item.rating
          });
        }
      }
    });

    if (!chartData.length) {
      container.innerHTML = '<p class="benchmark-no-data">No chart data available for this mode.</p>';
      return;
    }

    // Find max value for scaling
    let maxValue = 0;
    chartData.forEach(function(d) {
      maxValue = Math.max(maxValue, d.cgxValue, d.fastestValue);
    });

    let html = '<div class="benchmark-charts-container">';

    chartData.forEach(function(d) {
      const cgxPct = (d.cgxValue / maxValue) * 100;
      const fastestPct = (d.fastestValue / maxValue) * 100;

      html += '<div class="chart-row">';
      html += '<div class="chart-scenario">' + escapeHtml(d.scenario) + '</div>';
      html += '<div class="chart-bars">';

      // CodeGlyphX bar
      html += '<div class="chart-bar-row">';
      html += '<span class="chart-bar-label">CodeGlyphX</span>';
      html += '<div class="chart-bar-container">';
      html += '<div class="chart-bar chart-bar-cgx' + (d.isCgxFastest ? ' chart-bar-winner' : '') + '" style="width: ' + cgxPct + '%"></div>';
      html += '<span class="chart-bar-value">' + escapeHtml(d.cgxLabel) + '</span>';
      html += '</div></div>';

      // Fastest competitor bar (only if different from CodeGlyphX)
      if (!d.isCgxFastest) {
        html += '<div class="chart-bar-row">';
        html += '<span class="chart-bar-label">' + escapeHtml(d.fastestVendor) + '</span>';
        html += '<div class="chart-bar-container">';
        html += '<div class="chart-bar chart-bar-fastest" style="width: ' + fastestPct + '%"></div>';
        html += '<span class="chart-bar-value">' + escapeHtml(d.fastestLabel) + '</span>';
        html += '</div></div>';
      }

      html += '</div></div>';
    });

    html += '</div>';

    // Add chart legend
    html += '<div class="chart-legend">';
    html += '<span class="chart-legend-item"><span class="chart-legend-color chart-legend-cgx"></span> CodeGlyphX</span>';
    html += '<span class="chart-legend-item"><span class="chart-legend-color chart-legend-fastest"></span> Fastest Competitor</span>';
    html += '<span class="chart-legend-item"><span class="chart-legend-color chart-legend-winner"></span> Winner (fastest overall)</span>';
    html += '</div>';

    container.innerHTML = html;
  }

  function renderAll() {
    const summaryEntry = getEntry(summaryData, currentOs, currentMode);
    const detailEntry = getEntry(detailData, currentOs, currentMode);
    const entry = summaryEntry || detailEntry;

    renderMeta(entry);
    renderModeSelector(summaryData, detailData);
    renderSummaryTable(summaryEntry);
    renderCharts(summaryEntry);
    renderDetails(detailEntry);
    renderBaseline(detailEntry);
    renderEnvironment(entry);
    renderNotes(entry);
  }

  function init() {
    // Check if we're on the benchmark page
    if (!document.querySelector('.benchmark-page')) return;

    Promise.all([
      loadJson('/data/benchmark-summary.json'),
      loadJson('/data/benchmark.json')
    ]).then(function(results) {
      summaryData = results[0];
      detailData = results[1];

      // Find best available entry to set initial mode
      findBestEntry(summaryData) || findBestEntry(detailData);

      renderAll();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
