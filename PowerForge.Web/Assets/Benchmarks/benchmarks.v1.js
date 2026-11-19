// PowerForge benchmark components: run switching per comparison and on-demand loading of area pages.
// Without this script every published run is shown in turn and each area links to its own page.
(function () {
  'use strict';

  function initComparison(section) {
    if (section.hasAttribute('data-pf-bench-ready')) return;
    section.setAttribute('data-pf-bench-ready', '');
    var tabs = section.querySelector('[data-pf-bench-tabs]');
    var panels = section.querySelectorAll('[data-pf-bench-run]');
    if (!tabs || panels.length < 2) return;
    var buttons = tabs.querySelectorAll('[data-pf-bench-tab]');

    function select(id, focus) {
      for (var i = 0; i < panels.length; i++)
        panels[i].hidden = panels[i].getAttribute('data-pf-bench-run') !== id;
      for (var j = 0; j < buttons.length; j++) {
        var selected = buttons[j].getAttribute('data-pf-bench-tab') === id;
        buttons[j].setAttribute('aria-selected', selected ? 'true' : 'false');
        buttons[j].tabIndex = selected ? 0 : -1;
        if (selected && focus) buttons[j].focus();
      }
    }

    tabs.addEventListener('click', function (event) {
      var button = event.target.closest('[data-pf-bench-tab]');
      if (button) select(button.getAttribute('data-pf-bench-tab'), false);
    });
    tabs.addEventListener('keydown', function (event) {
      if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
      var list = Array.prototype.slice.call(buttons);
      var index = list.indexOf(document.activeElement);
      if (index < 0) return;
      var next = list[(index + (event.key === 'ArrowRight' ? 1 : list.length - 1)) % list.length];
      select(next.getAttribute('data-pf-bench-tab'), true);
      event.preventDefault();
    });

    tabs.hidden = false;
    select(section.getAttribute('data-pf-bench-default') || buttons[0].getAttribute('data-pf-bench-tab'), false);
  }

  // Relative addresses in an area page are relative to that page, not to the page it is shown in. They are made
  // absolute against the area page's address before its content moves. Fragment links, absolute URLs and data: or
  // mailto: addresses are kept as written; javascript: and vbscript: addresses are removed along with scripts.
  var scheme = /^[a-z][a-z0-9+.-]*:/i;
  var blocked = { 'javascript:': true, 'vbscript:': true };

  // What the URL parser sees: tabs and line breaks removed, and C0 controls and spaces trimmed from both ends.
  function clean(value) {
    return value.replace(/[\t\n\r]/g, '').replace(/^[\u0000- ]+|[\u0000- ]+$/g, '');
  }

  // Returns the address to keep, or null when it must be removed.
  function resolveUrl(value, base) {
    var url = clean(value);
    if (url === '' || url.charAt(0) === '#') return value;
    var parsed;
    try { parsed = new URL(url, base); } catch (error) { return value; }
    if (blocked[parsed.protocol.toLowerCase()]) return null;
    return scheme.test(url) || url.indexOf('//') === 0 ? value : parsed.href;
  }

  // Splits a srcset the way the HTML parser does: a URL runs to the next whitespace (a trailing comma ends the
  // candidate), and its descriptors run to the next comma outside parentheses. URLs may therefore contain commas.
  function parseSrcset(value) {
    var candidates = [];
    var i = 0;
    while (i < value.length) {
      while (i < value.length && /[\s,]/.test(value.charAt(i))) i++;
      if (i >= value.length) break;
      var start = i;
      while (i < value.length && !/\s/.test(value.charAt(i))) i++;
      var url = value.slice(start, i);
      var descriptors = '';
      if (/,$/.test(url)) {
        url = url.replace(/,+$/, '');
      } else {
        var from = i;
        var depth = 0;
        while (i < value.length) {
          var character = value.charAt(i);
          if (character === '(') depth++;
          else if (character === ')' && depth > 0) depth--;
          else if (character === ',' && depth === 0) break;
          i++;
        }
        descriptors = value.slice(from, i).trim();
        i++;
      }
      if (url) candidates.push({ url: url, descriptors: descriptors });
    }
    return candidates;
  }

  function resolveSrcset(value, base) {
    var kept = [];
    parseSrcset(value).forEach(function (candidate) {
      var url = resolveUrl(candidate.url, base);
      if (url !== null) kept.push(candidate.descriptors ? url + ' ' + candidate.descriptors : url);
    });
    return kept.join(', ');
  }

  function rebase(root, base) {
    var scripts = root.querySelectorAll('script');
    for (var i = 0; i < scripts.length; i++) scripts[i].remove();
    var elements = root.querySelectorAll('[href], [src], [srcset]');
    for (var j = 0; j < elements.length; j++) {
      var element = elements[j];
      ['href', 'src'].forEach(function (name) {
        var value = element.getAttribute(name);
        if (value === null) return;
        var url = resolveUrl(value, base);
        if (url === null) element.removeAttribute(name);
        else element.setAttribute(name, url);
      });
      var srcset = element.getAttribute('srcset');
      if (srcset !== null) element.setAttribute('srcset', resolveSrcset(srcset, base));
    }
  }

  // The link to the area page is already relative to the current page, so its resolved href is the page to load.
  function initArea(details) {
    if (details.hasAttribute('data-pf-bench-ready')) return;
    details.setAttribute('data-pf-bench-ready', '');
    details.addEventListener('toggle', function () {
      if (!details.open || details.hasAttribute('data-pf-bench-loaded')) return;
      var body = details.querySelector('[data-pf-bench-area-body]');
      var link = body && body.querySelector('a[href]');
      if (!link) return;
      var fallback = link.cloneNode(true);
      details.setAttribute('data-pf-bench-loaded', 'loading');
      body.textContent = 'Loading results...';
      fetch(link.href, { credentials: 'same-origin' })
        .then(function (response) {
          if (!response.ok) throw new Error('HTTP ' + response.status);
          return response.text().then(function (html) { return { html: html, url: response.url || link.href }; });
        })
        .then(function (loaded) {
          // The area page is rendered by this site's own build from the same templates as this page; its body,
          // without scripts and with its addresses rebased, becomes the results.
          var page = new DOMParser().parseFromString(loaded.html, 'text/html');
          rebase(page.body, loaded.url);
          body.replaceChildren.apply(body, Array.prototype.slice.call(page.body.childNodes));
          details.setAttribute('data-pf-bench-loaded', 'true');
          init(body);
        })
        .catch(function () {
          body.textContent = 'The results could not be loaded. ';
          body.appendChild(fallback);
          details.removeAttribute('data-pf-bench-loaded');
        });
    });
  }

  function init(root) {
    var sections = root.querySelectorAll('[data-pf-bench]');
    for (var i = 0; i < sections.length; i++) initComparison(sections[i]);
    var areas = root.querySelectorAll('[data-pf-bench-area-page]');
    for (var j = 0; j < areas.length; j++) initArea(areas[j]);
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', function () { init(document); });
  else
    init(document);
})();
