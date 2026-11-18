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
          return response.text();
        })
        .then(function (html) {
          // The area page is rendered by this site's own build from the same templates as this page; its body,
          // without scripts, becomes the results.
          var page = new DOMParser().parseFromString(html, 'text/html');
          var scripts = page.body.querySelectorAll('script');
          for (var i = 0; i < scripts.length; i++) scripts[i].remove();
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
