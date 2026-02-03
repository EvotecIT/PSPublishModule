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

        const loadScript = (src, done) => {
            const el = document.createElement('script');
            el.src = src;
            el.onload = () => done?.();
            document.head.appendChild(el);
        };

        const script = document.createElement('script');
        script.src = '/vendor/prism/prism.min.js';
        script.onload = () => {
            loadScript('/vendor/prism/prism-csharp.min.js', () => {
                // VB.NET grammar depends on the BASIC grammar.
                loadScript('/vendor/prism/prism-basic.min.js', () => {
                    loadScript('/vendor/prism/prism-vbnet.min.js', () => {
                        globalThis.__codeGlyphPrismLoaded = true;
                        callback?.();
                    });
                });
            });
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
            if (!block.classList.contains('code-block')) {
                block.classList.add('code-block');
            }
            const codeEl = block.querySelector('code');
            const hasHighlight = block.querySelector('.token, .keyword, .string, .comment');
            if (codeEl && !hasHighlight) {
                blocksNeedingHighlight.push({ block, codeEl });
            } else {
                blocksReady.push({ block, codeEl });
            }
        });

        // Add copy buttons to blocks that don't need highlighting
        blocksReady.forEach(({ block }) => addCopyButton(block));

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
                blocksNeedingHighlight.forEach(({ block, codeEl }) => {
                    if (!codeEl) {
                        addCopyButton(block);
                        return;
                    }
                    if (!codeEl.classList.contains('prism-highlighted')) {
                        const codeLanguage = Array.from(codeEl.classList).find((cls) => cls.startsWith('language-'));
                        const blockLanguage = Array.from(block.classList).find((cls) => cls.startsWith('language-'));
                        const targetLanguage = codeLanguage || blockLanguage || 'language-plain';
                        if (!codeLanguage) {
                            codeEl.classList.add(targetLanguage);
                        }
                        codeEl.classList.add('prism-highlighted');
                        globalThis.Prism?.highlightElement?.(codeEl);
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

// File drop handling for Blazor InputFile
globalThis.CodeGlyphX = globalThis.CodeGlyphX || {};
globalThis.CodeGlyphX.setupDropZone = function(dropZoneElement, inputFileElement) {
    if (!dropZoneElement || !inputFileElement) return;

    // Find the actual input element inside the InputFile component
    const inputElement = inputFileElement.querySelector('input[type="file"]') || inputFileElement;

    function handleDrop(e) {
        e.preventDefault();
        e.stopPropagation();

        if (e.dataTransfer?.files?.length > 0) {
            // Create a new DataTransfer to set files on the input
            const dt = new DataTransfer();
            for (let i = 0; i < e.dataTransfer.files.length; i++) {
                dt.items.add(e.dataTransfer.files[i]);
            }
            inputElement.files = dt.files;

            // Trigger change event
            inputElement.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    function handleDragOver(e) {
        e.preventDefault();
        e.stopPropagation();
    }

    dropZoneElement.addEventListener('drop', handleDrop);
    dropZoneElement.addEventListener('dragover', handleDragOver);

    return {
        dispose: function() {
            dropZoneElement.removeEventListener('drop', handleDrop);
            dropZoneElement.removeEventListener('dragover', handleDragOver);
        }
    };
};

// Theme toggle + cycle button (static pages)
(function() {
    var themeButtons = Array.prototype.slice.call(document.querySelectorAll('.theme-toggle button[data-theme]'));
    var cycleButton = document.querySelector('.theme-cycle-btn');
    var themeOrder = ['auto', 'light', 'dark'];
    var themeIcons = {
        auto: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>',
        light: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="5"/><path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"/></svg>',
        dark: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>'
    };

    function getThemeLocal() {
        return document.documentElement.dataset.theme || localStorage.getItem('theme') || 'auto';
    }

    function getCycleTitle(theme) {
        if (theme === 'light') return 'Light mode (click to switch to dark)';
        if (theme === 'dark') return 'Dark mode (click to switch to auto)';
        return 'Auto mode (click to switch to light)';
    }

    function updateActiveTheme(theme) {
        if (!themeButtons.length) return;
        themeButtons.forEach(function(btn) {
            btn.classList.toggle('active', btn.dataset.theme === theme);
        });
    }

    function updateCycleButton(theme) {
        if (!cycleButton) return;
        cycleButton.innerHTML = themeIcons[theme] || themeIcons.auto;
        var title = getCycleTitle(theme);
        cycleButton.setAttribute('title', title);
        cycleButton.setAttribute('aria-label', title);
    }

    function setThemeLocal(theme) {
        document.documentElement.dataset.theme = theme;
        localStorage.setItem('theme', theme);
        updateActiveTheme(theme);
        updateCycleButton(theme);
    }

    var currentTheme = getThemeLocal();
    updateActiveTheme(currentTheme);
    updateCycleButton(currentTheme);

    themeButtons.forEach(function(btn) {
        btn.addEventListener('click', function() {
            var theme = this.dataset.theme;
            if (!theme) return;
            setThemeLocal(theme);
        });
    });

    if (cycleButton) {
        cycleButton.addEventListener('click', function() {
            var theme = getThemeLocal();
            var idx = themeOrder.indexOf(theme);
            var next = themeOrder[(idx + 1) % themeOrder.length];
            setThemeLocal(next);
        });
    }
})();
