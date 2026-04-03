(function() {
  var sidebar = document.querySelector('.api-sidebar');
  var toggle = document.querySelector('.sidebar-toggle');
  var overlay = document.querySelector('.sidebar-overlay');
  var filterInput = document.querySelector('.sidebar-search input');
  var clearButton = document.querySelector('.clear-search');
  var emptyLabel = document.querySelector('.sidebar-empty');
  var kindButtons = Array.prototype.slice.call(document.querySelectorAll('.filter-button'));
  var namespaceSelect = document.querySelector('#api-namespace');
  var namespaceCombo = null;
  var countLabel = document.querySelector('.sidebar-count');
  var expandAllBtn = document.querySelector('.sidebar-expand-all');
  var collapseAllBtn = document.querySelector('.sidebar-collapse-all');
  var resetButton = document.querySelector('.sidebar-reset');
  var memberFilter = document.querySelector('#api-member-filter');
  var memberKindButtons = Array.prototype.slice.call(document.querySelectorAll('.member-kind'));
  var inheritedToggle = document.querySelector('#api-show-inherited');
  var memberExpandAll = document.querySelector('.member-expand-all');
  var memberCollapseAll = document.querySelector('.member-collapse-all');
  var memberReset = document.querySelector('.member-reset');
  var tocToggle = document.querySelector('.type-toc-toggle');
  var memberSectionToggles = Array.prototype.slice.call(document.querySelectorAll('.member-section-toggle'));
  var overviewGroupToggles = Array.prototype.slice.call(document.querySelectorAll('[data-overview-group-toggle]'));
  var suiteSearchRoot = document.querySelector('.api-suite-search[data-suite-search-url]');
  var suiteSearchInput = suiteSearchRoot ? suiteSearchRoot.querySelector('.api-suite-search-input') : null;
  var suiteSearchResults = suiteSearchRoot ? suiteSearchRoot.querySelector('.api-suite-search-results') : null;
  var suiteSearchStatus = suiteSearchRoot ? suiteSearchRoot.querySelector('.api-suite-search-status') : null;
  var suiteSearchFilterButtons = suiteSearchRoot ? Array.prototype.slice.call(suiteSearchRoot.querySelectorAll('.api-suite-search-filter')) : [];
  var suiteCoverageRoot = document.querySelector('.api-suite-coverage-summary[data-suite-coverage-url]');
  var suiteCoverageGrid = suiteCoverageRoot ? suiteCoverageRoot.querySelector('.api-suite-coverage-grid') : null;
  var suiteCoverageStatus = suiteCoverageRoot ? suiteCoverageRoot.querySelector('.api-suite-coverage-status') : null;
  var suiteNarrativeRoot = document.querySelector('.api-suite-narrative[data-suite-narrative-url]');
  var suiteNarrativeSummary = suiteNarrativeRoot ? suiteNarrativeRoot.querySelector('.api-suite-narrative-summary') : null;
  var suiteNarrativeSections = suiteNarrativeRoot ? suiteNarrativeRoot.querySelector('.api-suite-narrative-sections') : null;
  var suiteNarrativeStatus = suiteNarrativeRoot ? suiteNarrativeRoot.querySelector('.api-suite-narrative-status') : null;
  var suiteRelatedContentRoot = document.querySelector('.api-suite-related-content[data-suite-related-content-url]');
  var suiteRelatedContentList = suiteRelatedContentRoot ? suiteRelatedContentRoot.querySelector('.api-suite-related-content-list') : null;
  var suiteRelatedContentStatus = suiteRelatedContentRoot ? suiteRelatedContentRoot.querySelector('.api-suite-related-content-status') : null;

  function setSidebar(open) {
    if (!sidebar || !overlay) return;
    sidebar.classList.toggle('sidebar-open', open);
    overlay.classList.toggle('active', open);
  }

  if (toggle) {
    toggle.addEventListener('click', function() {
      var open = !sidebar || !sidebar.classList.contains('sidebar-open');
      setSidebar(open);
    });
  }

  if (overlay) {
    overlay.addEventListener('click', function() {
      setSidebar(false);
    });
  }

  function initNavDropdowns() {
    var dropdowns = Array.prototype.slice.call(document.querySelectorAll('.nav-dropdown'));
    if (!dropdowns.length) return;

    dropdowns.forEach(function(dropdown) {
      if (!(dropdown instanceof HTMLElement) || dropdown.dataset.bound === 'true') return;
      dropdown.dataset.bound = 'true';

      var trigger = dropdown.querySelector('.nav-dropdown-trigger');
      if (!(trigger instanceof HTMLElement)) return;

      trigger.setAttribute('aria-haspopup', 'menu');
      trigger.setAttribute('aria-expanded', 'false');

      function setExpanded(expanded) {
        trigger.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        dropdown.classList.toggle('open', expanded);
      }

      dropdown.addEventListener('mouseenter', function() { setExpanded(true); });
      dropdown.addEventListener('mouseleave', function() { setExpanded(false); });
      dropdown.addEventListener('focusin', function() { setExpanded(true); });
      dropdown.addEventListener('focusout', function() {
        setTimeout(function() {
          setExpanded(dropdown.contains(document.activeElement));
        }, 0);
      });
      dropdown.addEventListener('keydown', function(event) {
        if (event.key !== 'Escape') return;
        setExpanded(false);
        trigger.focus();
      });
    });
  }

  function normalize(text) {
    return (text || '').toLowerCase();
  }

  function formatCount(value) {
    var n = parseInt(value, 10);
    if (!Number.isFinite(n)) return '0';
    return n.toLocaleString();
  }

  function escapeHtml(text) {
    return String(text || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function readMetricNumber(root, path) {
    if (!root || !path) return 0;
    var current = root;
    var parts = String(path).split('.');
    for (var i = 0; i < parts.length; i++) {
      if (!current || typeof current !== 'object' || !(parts[i] in current)) return 0;
      current = current[parts[i]];
    }
    var n = Number(current);
    return Number.isFinite(n) ? n : 0;
  }

  function renderSuiteCoverageSummary() {
    if (!suiteCoverageRoot || !suiteCoverageGrid || !suiteCoverageStatus) return;
    var coverageUrl = suiteCoverageRoot.getAttribute('data-suite-coverage-url');
    if (!coverageUrl) return;

    suiteCoverageStatus.textContent = 'Loading suite coverage summary...';
    fetch(coverageUrl)
      .then(function(response) {
        if (!response.ok) throw new Error('HTTP ' + response.status);
        return response.json();
      })
      .then(function(payload) {
        var cards = [
          {
            label: 'Projects',
            value: formatCount(readMetricNumber(payload, 'projectCount')),
            note: 'APIs included in this suite portal.'
          },
          {
            label: 'Types',
            value: formatCount(readMetricNumber(payload, 'types.count')),
            note: 'Public symbols merged across all project APIs.'
          },
          {
            label: 'Commands',
            value: formatCount(readMetricNumber(payload, 'powershell.commandCount')),
            note: 'PowerShell commands currently represented in the suite.'
          },
          {
            label: 'Quick Start Coverage',
            value: readMetricNumber(payload, 'types.quickStartRelatedContent.percent').toFixed(0) + '%',
            note: 'Configured quick-start symbols with curated guidance attached.'
          },
          {
            label: 'Missing Quick Starts',
            value: formatCount(readMetricNumber(payload, 'types.quickStartMissingRelatedContent.count')),
            note: 'Important suite entry points still missing curated walkthroughs.'
          }
        ];

        suiteCoverageGrid.hidden = false;
        suiteCoverageGrid.innerHTML = cards.map(function(card) {
          return '<div class="api-suite-coverage-card">' +
            '<strong>' + escapeHtml(card.value) + '</strong>' +
            '<em>' + escapeHtml(card.label) + '</em>' +
            '<span>' + escapeHtml(card.note) + '</span>' +
            '</div>';
        }).join('');
        suiteCoverageStatus.textContent = 'Suite coverage summary loaded.';
      })
      .catch(function() {
        suiteCoverageStatus.textContent = 'Suite coverage summary is unavailable right now.';
      });
  }

  function renderSuiteNarrative() {
    if (!suiteNarrativeRoot || !suiteNarrativeSummary || !suiteNarrativeSections || !suiteNarrativeStatus) return;
    var narrativeUrl = suiteNarrativeRoot.getAttribute('data-suite-narrative-url');
    if (!narrativeUrl) return;

    suiteNarrativeStatus.textContent = 'Loading suite guidance...';
    fetch(narrativeUrl)
      .then(function(response) {
        if (!response.ok) throw new Error('HTTP ' + response.status);
        return response.json();
      })
      .then(function(payload) {
        var sections = Array.isArray(payload.sections) ? payload.sections : [];
        if (!sections.length) {
          suiteNarrativeStatus.textContent = 'No suite onboarding guidance has been attached yet.';
          return;
        }

        var intro = payload.summary || payload.description || '';
        if (intro) {
          suiteNarrativeSummary.hidden = false;
          suiteNarrativeSummary.textContent = intro;
        } else {
          suiteNarrativeSummary.hidden = true;
          suiteNarrativeSummary.textContent = '';
        }

        suiteNarrativeSections.hidden = false;
        suiteNarrativeSections.innerHTML = sections.map(function(section) {
          var sectionTitle = escapeHtml(section.title || 'Section');
          var sectionSummary = section.summary ? '<p>' + escapeHtml(section.summary) + '</p>' : '';
          var items = Array.isArray(section.items) ? section.items : [];
          var cards = items.map(function(item) {
            var title = escapeHtml(item.title || 'Guide');
            var href = escapeHtml(item.url || '#');
            var summary = item.summary ? '<span>' + escapeHtml(item.summary) + '</span>' : '';
            var kind = escapeHtml(item.kind || 'guide');
            var metaParts = [];
            if (Array.isArray(item.suiteEntryLabels) && item.suiteEntryLabels.length) {
              metaParts.push(item.suiteEntryLabels.join(', '));
            }
            if (item.audience) {
              metaParts.push(item.audience);
            }
            if (item.estimatedTime) {
              metaParts.push(item.estimatedTime);
            }
            var meta = metaParts.length
              ? '<div class="api-suite-narrative-meta">' + escapeHtml(metaParts.join(' · ')) + '</div>'
              : '';
            return '<a class="api-suite-narrative-item" href="' + href + '">' +
              '<strong>' + title + '</strong>' +
              '<em class="api-suite-narrative-kind">' + kind + '</em>' +
              meta +
              summary +
              '</a>';
          }).join('');
          return '<section class="api-suite-narrative-section">' +
            '<div class="api-suite-narrative-section-head">' +
            '<h3>' + sectionTitle + '</h3>' +
            sectionSummary +
            '</div>' +
            '<div class="api-suite-narrative-items">' + cards + '</div>' +
            '</section>';
        }).join('');
        suiteNarrativeStatus.textContent = 'Showing ' + formatCount(sections.length) + ' suite guidance section' + (sections.length === 1 ? '' : 's') + '.';
      })
      .catch(function() {
        suiteNarrativeStatus.textContent = 'Suite guidance is unavailable right now.';
      });
  }

  function renderSuiteRelatedContent() {
    if (!suiteRelatedContentRoot || !suiteRelatedContentList || !suiteRelatedContentStatus) return;
    var relatedUrl = suiteRelatedContentRoot.getAttribute('data-suite-related-content-url');
    if (!relatedUrl) return;

    suiteRelatedContentStatus.textContent = 'Loading curated guides...';
    fetch(relatedUrl)
      .then(function(response) {
        if (!response.ok) throw new Error('HTTP ' + response.status);
        return response.json();
      })
      .then(function(payload) {
        var items = Array.isArray(payload) ? payload : (Array.isArray(payload.items) ? payload.items : []);
        items = items.slice(0, 8);
        if (!items.length) {
          suiteRelatedContentStatus.textContent = 'No curated suite guides have been attached yet.';
          return;
        }

        suiteRelatedContentList.hidden = false;
        suiteRelatedContentList.innerHTML = items.map(function(item) {
          var title = escapeHtml(item.title || 'Guide');
          var summary = item.summary ? '<span>' + escapeHtml(item.summary) + '</span>' : '';
          var href = escapeHtml(item.url || '#');
          var suiteLabel = escapeHtml(item.suiteEntryLabel || item.suiteEntryId || 'API');
          var kind = escapeHtml(item.kind || 'guide');
          return '<a class="api-suite-related-content-item" href="' + href + '">' +
            '<strong>' + title + '</strong>' +
            '<em class="api-suite-related-content-kind">' + kind + '</em>' +
            '<div class="api-suite-related-content-meta">' + suiteLabel + '</div>' +
            summary +
            '</a>';
        }).join('');
        suiteRelatedContentStatus.textContent = 'Showing ' + formatCount(items.length) + ' curated suite guide' + (items.length === 1 ? '' : 's') + '.';
      })
      .catch(function() {
        suiteRelatedContentStatus.textContent = 'Curated suite guides are unavailable right now.';
      });
  }

  function initSuiteSearch() {
    if (!suiteSearchRoot || !suiteSearchInput || !suiteSearchResults || !suiteSearchStatus) return;
    var searchUrl = suiteSearchRoot.getAttribute('data-suite-search-url');
    if (!searchUrl) return;

    var suiteItems = null;
    var loading = false;
    var loaded = false;
    var lastQuery = '';
    var activeSuiteFilter = '';

    function setStatus(text) {
      suiteSearchStatus.textContent = text || '';
    }

    function getActiveFilterLabel() {
      for (var i = 0; i < suiteSearchFilterButtons.length; i++) {
        if (suiteSearchFilterButtons[i].classList.contains('active')) {
          return suiteSearchFilterButtons[i].textContent || '';
        }
      }
      return 'All APIs';
    }

    function setActiveFilter(value) {
      activeSuiteFilter = value || '';
      suiteSearchFilterButtons.forEach(function(button) {
        var isActive = (button.getAttribute('data-suite-search-filter') || '') === activeSuiteFilter;
        button.classList.toggle('active', isActive);
      });
    }

    function renderSuiteResults(query) {
      var q = normalize(query);
      if (!q) {
        suiteSearchResults.hidden = true;
        suiteSearchResults.innerHTML = '';
        if (activeSuiteFilter) {
          setStatus('Filter set to ' + getActiveFilterLabel() + '. Start typing to search.');
        } else {
          setStatus('Start typing to search across the full API suite.');
        }
        return;
      }

      var items = (suiteItems || []).filter(function(item) {
        if (activeSuiteFilter && normalize(item.suiteEntryId) !== normalize(activeSuiteFilter)) {
          return false;
        }
        var haystack = normalize([
          item.title,
          item.displayName,
          item.summary,
          item.kind,
          item.namespace,
          item.suiteEntryLabel,
          item.suiteEntryId,
          Array.isArray(item.aliases) ? item.aliases.join(' ') : ''
        ].join(' '));
        return haystack.indexOf(q) !== -1;
      }).slice(0, 8);

      if (!items.length) {
        suiteSearchResults.hidden = false;
        suiteSearchResults.innerHTML = '<div class="api-suite-search-empty">No suite matches found.</div>';
        setStatus('No matching symbols found' + (activeSuiteFilter ? ' in ' + getActiveFilterLabel() : ' across this API suite') + '.');
        return;
      }

      suiteSearchResults.hidden = false;
      suiteSearchResults.innerHTML = items.map(function(item) {
        var title = escapeHtml(item.displayName || item.title || item.slug || 'Result');
        var label = escapeHtml(item.suiteEntryLabel || item.suiteEntryId || 'API');
        var summary = item.summary ? '<span>' + escapeHtml(item.summary) + '</span>' : '';
        var href = escapeHtml(item.url || '#');
        return '<a class="api-suite-search-result" href="' + href + '">' +
          '<strong>' + title + '</strong>' +
          '<em>' + label + '</em>' +
          summary +
          '</a>';
      }).join('');
      setStatus('Showing ' + formatCount(items.length) + ' suite result' + (items.length === 1 ? '' : 's') + (activeSuiteFilter ? ' in ' + getActiveFilterLabel() : '') + '.');
    }

    function loadSuiteItems() {
      if (loaded || loading) return;
      loading = true;
      setStatus('Loading suite search index...');
      fetch(searchUrl)
        .then(function(response) {
          if (!response.ok) throw new Error('HTTP ' + response.status);
          return response.json();
        })
        .then(function(payload) {
          suiteItems = Array.isArray(payload) ? payload : (Array.isArray(payload.items) ? payload.items : []);
          loaded = true;
          loading = false;
          if (!suiteItems.length) {
            setStatus('Suite search index is available but has no items yet.');
            return;
          }
          renderSuiteResults(lastQuery);
        })
        .catch(function() {
          loading = false;
          setStatus('Suite search is unavailable right now.');
        });
    }

    suiteSearchInput.addEventListener('focus', loadSuiteItems, { once: true });
    suiteSearchFilterButtons.forEach(function(button) {
      button.addEventListener('click', function() {
        setActiveFilter(button.getAttribute('data-suite-search-filter') || '');
        renderSuiteResults(lastQuery);
      });
    });
    suiteSearchInput.addEventListener('input', function() {
      lastQuery = suiteSearchInput.value || '';
      if (!loaded) {
        loadSuiteItems();
      }
      if (loaded) {
        renderSuiteResults(lastQuery);
      } else if (lastQuery) {
        setStatus('Loading suite search index...');
      } else {
        if (activeSuiteFilter) {
          setStatus('Filter set to ' + getActiveFilterLabel() + '. Start typing to search.');
        } else {
          setStatus('Start typing to search across the full API suite.');
        }
      }
    });

    setActiveFilter('');
    setStatus('Start typing to search across the full API suite.');
  }

  function normalizePath(path) {
    if (!path) return '/';
    if (path === '/') return '/';
    return path.endsWith('/') ? path : path + '/';
  }

  function getApiDocsBasePath() {
    var sidebarTitle = document.querySelector('.sidebar-title[href]');
    var href = sidebarTitle ? sidebarTitle.getAttribute('href') : null;

    try {
      return normalizePath(new URL(href || window.location.pathname, window.location.href).pathname);
    } catch (error) {
      return normalizePath(window.location.pathname);
    }
  }

  function isApiDocLink(anchor) {
    var href = anchor && anchor.getAttribute('href');
    if (!href || href.charAt(0) === '#') return false;

    try {
      var url = new URL(href, window.location.href);
      var basePath = getApiDocsBasePath();
      var normalizedPath = normalizePath(url.pathname);
      return url.origin === window.location.origin && (normalizedPath === basePath || normalizedPath.indexOf(basePath) === 0);
    } catch (error) {
      return false;
    }
  }

  function isStateHash(hash) {
    return /^#(?:k|ns|q|mk|mq|mi|mc|tc)=/.test(hash || '');
  }

  function buildStateHash() {
    var parts = [];
    if (activeKind) parts.push('k=' + encodeURIComponent(activeKind));
    if (activeNamespace) parts.push('ns=' + encodeURIComponent(activeNamespace));
    if (filterInput && filterInput.value) parts.push('q=' + encodeURIComponent(filterInput.value));
    if (activeMemberKind) parts.push('mk=' + encodeURIComponent(activeMemberKind));
    if (memberFilter && memberFilter.value) parts.push('mq=' + encodeURIComponent(memberFilter.value));
    if (inheritedToggle && inheritedToggle.checked) parts.push('mi=1');
    if (window.__pfMemberCollapsed) parts.push('mc=' + encodeURIComponent(window.__pfMemberCollapsed));
    if (window.__pfTocCollapsed) parts.push('tc=1');

    return parts.length ? '#' + parts.join('&') : '';
  }

  function syncApiLinksWithState(hash) {
    document.querySelectorAll('a[href]').forEach(function(anchor) {
      if (!isApiDocLink(anchor)) return;

      var url = new URL(anchor.getAttribute('href'), window.location.href);
      if (url.hash && !isStateHash(url.hash)) return;

      url.hash = hash;
      anchor.setAttribute('href', url.pathname + url.search + url.hash);
    });
  }

  var activeKind = '';
  var activeNamespace = '';
  var activeMemberKind = '';
  var totalTypes = countLabel ? parseInt(countLabel.dataset.total || '0', 10) : 0;
  namespaceCombo = initNamespaceCombobox(namespaceSelect);

  function initNamespaceCombobox(select) {
    if (!select) return null;
    if (select.dataset.enhancedCombobox === 'true') return null;
    var options = Array.prototype.slice.call(select.options || []);
    if (!options.length) return null;

    var wrapper = document.createElement('div');
    wrapper.className = 'pf-combobox';
    var trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'pf-combobox-trigger';
    trigger.setAttribute('aria-haspopup', 'listbox');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.setAttribute('aria-label', 'Namespace filter');

    var panel = document.createElement('div');
    panel.className = 'pf-combobox-panel';
    panel.hidden = true;
    var list = document.createElement('div');
    list.className = 'pf-combobox-list';
    list.setAttribute('role', 'listbox');
    list.tabIndex = -1;
    panel.appendChild(list);

    var optionButtons = [];

    function closePanel() {
      wrapper.classList.remove('open');
      panel.hidden = true;
      trigger.setAttribute('aria-expanded', 'false');
    }

    function openPanel() {
      wrapper.classList.add('open');
      panel.hidden = false;
      trigger.setAttribute('aria-expanded', 'true');
      var selected = getSelectedOptionButton();
      if (selected) {
        selected.focus();
        selected.scrollIntoView({ block: 'nearest' });
      } else if (optionButtons.length) {
        optionButtons[0].focus();
      }
    }

    function getSelectedOptionButton() {
      for (var i = 0; i < optionButtons.length; i++) {
        if (optionButtons[i].classList.contains('active')) return optionButtons[i];
      }
      return null;
    }

    function moveFocus(direction) {
      if (!optionButtons.length) return;
      var current = document.activeElement;
      var index = optionButtons.indexOf(current);
      if (index < 0) {
        var selected = getSelectedOptionButton();
        index = selected ? optionButtons.indexOf(selected) : 0;
      }
      var next = index + direction;
      if (next < 0) next = optionButtons.length - 1;
      if (next >= optionButtons.length) next = 0;
      optionButtons[next].focus();
      optionButtons[next].scrollIntoView({ block: 'nearest' });
    }

    function syncFromSelect() {
      var selected = options.find(function(opt) { return opt.value === select.value; });
      if (!selected) {
        selected = select.options[select.selectedIndex] || options[0];
      }
      if (!selected) return;

      trigger.textContent = selected.textContent || selected.label || '';
      optionButtons.forEach(function(btn) {
        var isActive = btn.dataset.value === selected.value;
        btn.classList.toggle('active', isActive);
        btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
      });
    }

    function setValue(value) {
      if (select.value === value) return;
      select.value = value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
    }

    options.forEach(function(option) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'pf-combobox-option';
      btn.dataset.value = option.value || '';
      btn.setAttribute('role', 'option');
      btn.setAttribute('aria-selected', 'false');
      btn.textContent = option.textContent || option.label || '';
      if (option.disabled) btn.disabled = true;
      btn.addEventListener('click', function() {
        if (btn.disabled) return;
        setValue(btn.dataset.value || '');
        closePanel();
        trigger.focus();
      });
      list.appendChild(btn);
      optionButtons.push(btn);
    });

    trigger.addEventListener('click', function() {
      if (wrapper.classList.contains('open')) {
        closePanel();
      } else {
        openPanel();
      }
    });

    trigger.addEventListener('keydown', function(event) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        if (!wrapper.classList.contains('open')) openPanel();
        moveFocus(event.key === 'ArrowDown' ? 1 : -1);
      }
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        if (wrapper.classList.contains('open')) closePanel();
        else openPanel();
      }
      if (event.key === 'Escape') closePanel();
    });

    list.addEventListener('keydown', function(event) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        moveFocus(event.key === 'ArrowDown' ? 1 : -1);
      }
      if (event.key === 'Home') {
        event.preventDefault();
        if (optionButtons.length) optionButtons[0].focus();
      }
      if (event.key === 'End') {
        event.preventDefault();
        if (optionButtons.length) optionButtons[optionButtons.length - 1].focus();
      }
      if (event.key === 'Enter' || event.key === ' ') {
        var focused = document.activeElement;
        if (focused && focused.classList.contains('pf-combobox-option')) {
          event.preventDefault();
          focused.click();
        }
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        closePanel();
        trigger.focus();
      }
    });

    document.addEventListener('click', function(event) {
      if (!wrapper.contains(event.target)) closePanel();
    });

    select.dataset.enhancedCombobox = 'true';
    select.classList.add('pf-enhanced-native');
    select.insertAdjacentElement('afterend', wrapper);
    wrapper.appendChild(trigger);
    wrapper.appendChild(panel);
    syncFromSelect();

    return { sync: syncFromSelect };
  }

  function syncNamespaceCombobox() {
    if (namespaceCombo && typeof namespaceCombo.sync === 'function') namespaceCombo.sync();
  }

  function loadState() {
    var hash = window.location.hash || '';
    if (!hash) return;
    var query = hash.indexOf('?') >= 0 ? hash.split('?')[1] : hash.slice(1);
    if (!query) return;
    query.split('&').forEach(function(pair) {
      var parts = pair.split('=');
      if (parts.length < 2) return;
      var key = decodeURIComponent(parts[0]);
      var value = decodeURIComponent(parts.slice(1).join('='));
      if (key === 'k') activeKind = value;
      if (key === 'ns') activeNamespace = value;
      if (key === 'q' && filterInput) filterInput.value = value;
      if (key === 'mk') activeMemberKind = value;
      if (key === 'mq' && memberFilter) memberFilter.value = value;
      if (key === 'mi' && inheritedToggle) inheritedToggle.checked = value === '1';
      if (key === 'mc') window.__pfMemberCollapsed = value;
      if (key === 'tc') window.__pfTocCollapsed = value === '1';
    });
  }

  function saveState() {
    var hash = buildStateHash();
    if (!hash) {
      history.replaceState(null, '', window.location.pathname + window.location.search);
      syncApiLinksWithState('');
      return;
    }
    history.replaceState(null, '', window.location.pathname + window.location.search + hash);
    syncApiLinksWithState(hash);
  }

  function applyFilter(query) {
    var q = normalize(query);
    var items = Array.prototype.slice.call(document.querySelectorAll('.type-item'));
    var chips = Array.prototype.slice.call(document.querySelectorAll('.type-chip'));
    var sections = Array.prototype.slice.call(document.querySelectorAll('.nav-section'));

    items.forEach(function(item) {
      var hay = normalize(item.dataset.search || item.textContent);
      var matchSearch = !q || hay.indexOf(q) !== -1;
      var matchKind = !activeKind || item.dataset.kind === activeKind;
      var matchNamespace = !activeNamespace || item.dataset.namespace === activeNamespace;
      var match = matchSearch && matchKind && matchNamespace;
      item.style.display = match ? '' : 'none';
    });

    chips.forEach(function(chip) {
      var hay = normalize(chip.dataset.search || chip.textContent);
      var matchSearch = !q || hay.indexOf(q) !== -1;
      var matchKind = !activeKind || chip.dataset.kind === activeKind;
      var matchNamespace = !activeNamespace || chip.dataset.namespace === activeNamespace;
      var match = matchSearch && matchKind && matchNamespace;
      chip.style.display = match ? '' : 'none';
    });

    sections.forEach(function(section) {
      var hasVisible = false;
      section.querySelectorAll('.type-item').forEach(function(item) {
        if (item.style.display !== 'none') {
          hasVisible = true;
        }
      });
      section.style.display = hasVisible ? '' : 'none';
    });

    if (emptyLabel) {
      var anyVisible = items.some(function(item) { return item.style.display !== 'none'; });
      emptyLabel.hidden = anyVisible;
    }

    if (countLabel) {
      var visibleCount = items.filter(function(item) { return item.style.display !== 'none'; }).length;
      var total = totalTypes || items.length;
      countLabel.textContent = 'Showing ' + formatCount(visibleCount) + ' of ' + formatCount(total) + ' types';
    }
    saveState();
  }

  if (filterInput) {
    filterInput.addEventListener('input', function() {
      applyFilter(filterInput.value);
      if (clearButton) {
        clearButton.style.display = filterInput.value ? 'inline-flex' : 'none';
      }
    });
  }

  if (clearButton && filterInput) {
    clearButton.addEventListener('click', function() {
      filterInput.value = '';
      applyFilter('');
      clearButton.style.display = 'none';
      filterInput.focus();
    });
    clearButton.style.display = filterInput.value ? 'inline-flex' : 'none';
  }

  if (kindButtons.length) {
    kindButtons.forEach(function(btn) {
      btn.addEventListener('click', function() {
        kindButtons.forEach(function(b) { b.classList.remove('active'); });
        btn.classList.add('active');
        activeKind = btn.dataset.kind || '';
        applyFilter(filterInput ? filterInput.value : '');
      });
    });
  }

  if (namespaceSelect) {
    namespaceSelect.addEventListener('change', function() {
      activeNamespace = namespaceSelect.value || '';
      syncNamespaceCombobox();
      applyFilter(filterInput ? filterInput.value : '');
    });
  }

  if (resetButton) {
    resetButton.addEventListener('click', function() {
      activeKind = '';
      activeNamespace = '';
      if (filterInput) {
        filterInput.value = '';
      }
      if (namespaceSelect) {
        namespaceSelect.value = '';
      }
      syncNamespaceCombobox();
      kindButtons.forEach(function(b) { b.classList.remove('active'); });
      if (kindButtons.length) {
        kindButtons[0].classList.add('active');
      }
      if (clearButton) {
        clearButton.style.display = 'none';
      }
      applyFilter('');
    });
  }

  document.querySelectorAll('.nav-section-header').forEach(function(header) {
    header.addEventListener('click', function() {
      var section = header.closest('.nav-section');
      if (!section) return;
      var content = section.querySelector('.nav-section-content');
      var chevron = header.querySelector('.chevron');
      if (!content) return;
      var collapsed = content.classList.toggle('collapsed');
      content.hidden = collapsed;
      if (chevron) {
        chevron.classList.toggle('expanded', !collapsed);
      }
    });
  });

  function setAllSections(collapsed) {
    var sections = Array.prototype.slice.call(document.querySelectorAll('.nav-section'));
    sections.forEach(function(section) {
      var content = section.querySelector('.nav-section-content');
      if (!content) return;
      var chevron = section.querySelector('.chevron');
      if (collapsed) {
        content.classList.add('collapsed');
        content.hidden = true;
        if (chevron) chevron.classList.remove('expanded');
      } else {
        content.classList.remove('collapsed');
        content.hidden = false;
        if (chevron) chevron.classList.add('expanded');
      }
    });
  }

  if (expandAllBtn) {
    expandAllBtn.addEventListener('click', function() {
      setAllSections(false);
    });
  }

  if (collapseAllBtn) {
    collapseAllBtn.addEventListener('click', function() {
      setAllSections(true);
    });
  }

  function applyMemberFilter() {
    var q = normalize(memberFilter ? memberFilter.value : '');
    var showInherited = inheritedToggle ? inheritedToggle.checked : true;
    var cards = Array.prototype.slice.call(document.querySelectorAll('.member-card'));
    var sections = Array.prototype.slice.call(document.querySelectorAll('.member-section'));
    var groups = Array.prototype.slice.call(document.querySelectorAll('.member-group'));

    cards.forEach(function(card) {
      var hay = normalize(card.dataset.search || card.textContent);
      var matchSearch = !q || hay.indexOf(q) !== -1;
      var matchKind = !activeMemberKind || card.dataset.kind === activeMemberKind;
      var matchInherited = showInherited || card.dataset.inherited !== 'true';
      var match = matchSearch && matchKind && matchInherited;
      card.style.display = match ? '' : 'none';
    });

    groups.forEach(function(group) {
      var hasVisible = false;
      group.querySelectorAll('.member-card').forEach(function(card) {
        if (card.style.display !== 'none') hasVisible = true;
      });
      group.style.display = hasVisible ? '' : 'none';
    });

    sections.forEach(function(section) {
      var visible = false;
      section.querySelectorAll('.member-card').forEach(function(card) {
        if (card.style.display !== 'none') visible = true;
      });
      section.style.display = visible ? '' : 'none';
    });
    saveState();
  }

  if (memberFilter) {
    memberFilter.addEventListener('input', function() {
      applyMemberFilter();
      saveState();
    });
  }

  if (memberKindButtons.length) {
    memberKindButtons.forEach(function(btn) {
      btn.addEventListener('click', function() {
        memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
        btn.classList.add('active');
        activeMemberKind = btn.dataset.memberKind || '';
        applyMemberFilter();
        saveState();
      });
    });
  }

  if (inheritedToggle) {
    inheritedToggle.addEventListener('change', function() {
      applyMemberFilter();
      saveState();
    });
  }

  function updateMemberCollapsedState() {
    var collapsedKeys = [];
    document.querySelectorAll('.member-section').forEach(function(section) {
      if (section.classList.contains('collapsed')) {
        var id = section.getAttribute('id');
        if (id) collapsedKeys.push(id);
      }
    });
    window.__pfMemberCollapsed = collapsedKeys.join(',');
  }

  memberSectionToggles.forEach(function(btn) {
    btn.addEventListener('click', function() {
      var section = btn.closest('.member-section');
      if (!section) return;
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      var collapsed = section.classList.toggle('collapsed');
      body.hidden = collapsed;
      updateMemberCollapsedState();
      saveState();
    });
  });

  function setMemberSections(collapsed) {
    document.querySelectorAll('.member-section').forEach(function(section) {
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      if (collapsed) {
        section.classList.add('collapsed');
        body.hidden = true;
      } else {
        section.classList.remove('collapsed');
        body.hidden = false;
      }
    });
    updateMemberCollapsedState();
    saveState();
  }

  if (memberExpandAll) {
    memberExpandAll.addEventListener('click', function() {
      setMemberSections(false);
    });
  }

  if (memberCollapseAll) {
    memberCollapseAll.addEventListener('click', function() {
      setMemberSections(true);
    });
  }

  if (memberReset) {
    memberReset.addEventListener('click', function() {
      activeMemberKind = '';
      if (memberFilter) memberFilter.value = '';
      if (memberKindButtons.length) {
        memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
        memberKindButtons[0].classList.add('active');
      }
      if (inheritedToggle) inheritedToggle.checked = false;
      applyMemberFilter();
      saveState();
    });
  }

  if (tocToggle) {
    tocToggle.addEventListener('click', function() {
      var toc = tocToggle.closest('.type-toc');
      if (!toc) return;
      toc.classList.toggle('collapsed');
      window.__pfTocCollapsed = toc.classList.contains('collapsed');
      saveState();
    });
  }

  if (overviewGroupToggles.length) {
    overviewGroupToggles.forEach(function(toggleButton) {
      function syncOverviewGroup(expanded) {
        var group = toggleButton.closest('[data-overview-group]');
        if (!group) return;
        var extras = Array.prototype.slice.call(group.querySelectorAll('[data-overview-extra]'));
        extras.forEach(function(node) {
          node.hidden = !expanded;
        });
        toggleButton.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        toggleButton.textContent = expanded
          ? (toggleButton.getAttribute('data-collapse-label') || 'Show fewer')
          : (toggleButton.getAttribute('data-expand-label') || 'Show more');
      }

      syncOverviewGroup(false);
      toggleButton.addEventListener('click', function() {
        var expanded = toggleButton.getAttribute('aria-expanded') === 'true';
        syncOverviewGroup(!expanded);
      });
    });
  }

  loadState();
  if (window.__pfTocCollapsed && tocToggle) {
    var toc = tocToggle.closest('.type-toc');
    if (toc) toc.classList.add('collapsed');
  }
  if (window.__pfMemberCollapsed) {
    var collapsedIds = window.__pfMemberCollapsed.split(',').filter(Boolean);
    collapsedIds.forEach(function(id) {
      var section = document.getElementById(id);
      if (!section) return;
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      section.classList.add('collapsed');
      body.hidden = true;
    });
  }
  if (kindButtons.length) {
    kindButtons.forEach(function(b) { b.classList.remove('active'); });
    var activeBtn = kindButtons.find(function(b) { return (b.dataset.kind || '') === activeKind; });
    (activeBtn || kindButtons[0]).classList.add('active');
  }
  if (namespaceSelect) namespaceSelect.value = activeNamespace || '';
  syncNamespaceCombobox();
  if (memberKindButtons.length) {
    memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
    var activeMemberBtn = memberKindButtons.find(function(b) { return (b.dataset.memberKind || '') === activeMemberKind; });
    (activeMemberBtn || memberKindButtons[0]).classList.add('active');
  }
  initSuiteSearch();
  renderSuiteNarrative();
  renderSuiteCoverageSummary();
  renderSuiteRelatedContent();
  initNavDropdowns();
  applyFilter(filterInput ? filterInput.value : '');
  applyMemberFilter();
})();
