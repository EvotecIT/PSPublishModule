// PowerForge benchmark components: run switching per comparison and on-demand loading of area files.
// Without this script every published run is shown in turn and areas render inline or stay closed.
(function () {
  'use strict';

  // Area file paths are relative to the site root: the URL of this script without assets/powerforge/....
  var scriptPath = 'assets/powerforge/benchmarks.v1.js';
  var source = document.currentScript && document.currentScript.src;
  var siteRoot = source && source.lastIndexOf(scriptPath) >= 0
    ? source.slice(0, source.lastIndexOf(scriptPath))
    : document.baseURI;

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

  function initArea(details) {
    if (details.hasAttribute('data-pf-bench-ready')) return;
    details.setAttribute('data-pf-bench-ready', '');
    details.addEventListener('toggle', function () {
      if (!details.open || details.hasAttribute('data-pf-bench-loaded')) return;
      var body = details.querySelector('[data-pf-bench-area-body]');
      if (!body) return;
      details.setAttribute('data-pf-bench-loaded', 'loading');
      body.textContent = 'Loading results...';
      fetch(new URL(details.getAttribute('data-pf-bench-area-src'), siteRoot).href, { credentials: 'same-origin' })
        .then(function (response) {
          if (!response.ok) throw new Error('HTTP ' + response.status);
          return response.text();
        })
        .then(function (html) {
          // The area file is rendered by this site's own build from the same templates as the page.
          body.innerHTML = html;
          details.setAttribute('data-pf-bench-loaded', 'true');
          init(body);
        })
        .catch(function () {
          body.textContent = 'The results could not be loaded.';
          details.removeAttribute('data-pf-bench-loaded');
        });
    });
  }

  function init(root) {
    var sections = root.querySelectorAll('[data-pf-bench]');
    for (var i = 0; i < sections.length; i++) initComparison(sections[i]);
    var areas = root.querySelectorAll('[data-pf-bench-area-src]');
    for (var j = 0; j < areas.length; j++) initArea(areas[j]);
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', function () { init(document); });
  else
    init(document);
})();
